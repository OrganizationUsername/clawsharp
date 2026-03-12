using System.Diagnostics.CodeAnalysis;
using Clawsharp.Auth;
using Clawsharp.Channels;
using Clawsharp.Channels.BlueBubbles;
using Clawsharp.Channels.Cli;
using Clawsharp.Channels.Discord;
using Clawsharp.Channels.Email;
using Clawsharp.Channels.Irc;
using Clawsharp.Channels.Lark;
using Clawsharp.Channels.Line;
using Clawsharp.Channels.Matrix;
using Clawsharp.Channels.Mattermost;
using Clawsharp.Channels.Nostr;
using Clawsharp.Channels.Qq;
using Clawsharp.Channels.Signal;
using Clawsharp.Channels.Slack;
using Clawsharp.Channels.Telegram;
using Clawsharp.Channels.Web;
using Clawsharp.Channels.WeChat;
using Clawsharp.Channels.WeCom;
using Clawsharp.Channels.WhatsApp;
using Clawsharp.Config;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Core.Transcription;
using Clawsharp.Cost;
using Clawsharp.Cron;
using Clawsharp.Goals;
using Clawsharp.Ipc;
using Clawsharp.Memory;
using Clawsharp.Memory.Markdown;
using Clawsharp.Memory.MsSql;
using Clawsharp.Memory.Postgres;
using Clawsharp.Memory.Sqlite;
using Clawsharp.Providers;
using Clawsharp.Security;
using Clawsharp.Tools;
using Clawsharp.Tools.Browser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
// ReSharper disable once RedundantUsingDirective — provides UseVector() extension for NpgsqlDbContextOptionsBuilder
using Pgvector.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Remora.Discord.API.Abstractions.Gateway.Commands;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Hosting.Extensions;
using Spectre.Console;
using clawsharp;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Channels;
using Clawsharp.Config.Features;
using Clawsharp.Config.Memory;
using Clawsharp.Analytics;
using Clawsharp.Analytics.MsSql;
using Clawsharp.Analytics.Postgres;
using Clawsharp.Analytics.Sqlite;
using Clawsharp.Tools.Mcp;

namespace Clawsharp.Cli;

/// <summary>
///     Builds and runs the Generic Host that powers the AI assistant gateway.
///     Extracted from Program.cs so multiple Spectre commands can invoke it.
/// </summary>
public static partial class GatewayHost
{
    [RequiresUnreferencedCode(
        "Calls ValidateDataAnnotations which uses reflection to inspect DataAnnotation attributes. " +
        "AppConfig and AgentDefaults are statically referenced so members will not be trimmed.")]
    [RequiresDynamicCode(
        "Constructs EF Core DbContext-derived types (SqliteMemory, PostgresMemory, MsSqlMemory) " +
        "which call MigrateAsync and require dynamic code generation for query compilation.")]
    public static async Task RunAsync(CancellationToken ct = default)
    {
        // Build layered IConfiguration from all sources
        var configuration = ClawsharpConfiguration.Build();

        // Eagerly bind AppConfig for service registration decisions and pre-host validation.
        // Decrypt enc2: secrets so pre-host checks (discordEnabled, embeddingProvider, etc.) use real values.
        var appConfig = configuration.Get<AppConfig>() ?? new AppConfig();
        ClawsharpConfiguration.DecryptSecrets(appConfig);

        // Configure PromptGuard with any custom patterns from config (no-op if none configured).
        PromptGuard.Configure(appConfig.Security?.PromptGuard);

        // Validate config before building the host — no DI yet, use Console for these pre-host errors
        var validationErrors = ConfigValidator.Validate(appConfig);
        if (validationErrors.Count > 0)
        {
            foreach (var err in validationErrors)
            {
                AnsiConsole.MarkupLine($"[red]Config validation error:[/] {Markup.Escape(err)}");
            }

            Environment.ExitCode = 1;
            return;
        }

        // Apply Landlock LSM process-wide filesystem restrictions (Linux 5.13+ only).
        // Must run before the Generic Host is built so restrictions cover all subsequent I/O.
        // No-op if security.landlock.enabled = false (the default) or on non-Linux.
        if (OperatingSystem.IsLinux())
        {
            using var landlockLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
            var landlockLogger = landlockLoggerFactory.CreateLogger("Landlock");
            // shellEnabled=true preserves existing behavior; pass false to deny /bin /sbin execute access
            var shellEnabled = true; // TODO: derive from config when a "disable shell tool" toggle exists
            LandlockSandbox.Apply(appConfig.Security?.Landlock ?? new LandlockConfig(), landlockLogger, shellEnabled);
        }

        appConfig.Channels.TryGetValue("discord", out var discordCfg);
        var discordEnabled = discordCfg is { Enabled: true, Token: not null };

        // Pass Array.Empty so the Generic Host doesn't try to parse clawsharp's CLI args
        var hostBuilder = Host.CreateDefaultBuilder(Array.Empty<string>())
                              .ConfigureLogging(logging =>
                              {
                                  // Remove the default Console provider so log messages don't
                                  // interleave with the CLI channel's interactive prompt.
                                  // Logs are still visible in a debugger via the Debug provider.
                                  logging.ClearProviders();
                                  logging.AddDebug();
                                  logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                                  logging.AddFilter("Remora", LogLevel.Warning);
                                  logging.SetMinimumLevel(LogLevel.Information);
                              })
                              .ConfigureServices((_, services) =>
                              {
                                  // Limit how long the host waits for services to stop before
                                  // abandoning them. Prevents indefinite hangs from blocking
                                  // calls (Console.ReadLine, WebSocket close, in-flight LLM).
                                  services.Configure<HostOptions>(hostOpts =>
                                  {
                                      hostOpts.ShutdownTimeout = TimeSpan.FromSeconds(10);
                                  });
                                  // SSRF-safe ConnectCallback: re-validates resolved IPs at TCP connect
                                  // time, eliminating DNS rebinding (TOCTOU) attacks. Applied to all
                                  // named HTTP clients that may reach user-controlled or external URLs.
                                  var ssrfConnectCallback = SsrfGuard.CreateConnectCallback();

                                  // Resolve proxy URL: explicit config > HTTPS_PROXY > HTTP_PROXY > ALL_PROXY.
                                  // Applied only to provider/tool clients, not to channel clients.
                                  var proxyUrl = appConfig.HttpRequest?.Proxy
                                                 ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                                                 ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                                                 ?? Environment.GetEnvironmentVariable("ALL_PROXY");
                                  var webProxy = proxyUrl is not null ? new System.Net.WebProxy(proxyUrl) : null;

                                  // Local helper — registers a named HttpClient with the SSRF-safe
                                  // SocketsHttpHandler so every channel/provider benefits from the same
                                  // TOCTOU-safe connect guard, timeout, and optional BaseAddress.
                                  // This is the TypedHttpClient pattern applied to named clients:
                                  // BaseAddress is configured centrally here; consumers use relative paths.
                                  IHttpClientBuilder AddSsrfSafeHttpClient(
                                      string name,
                                      int timeoutSeconds = 30,
                                      Action<HttpClient>? configure = null,
                                      Action<SocketsHttpHandler>? configureHandler = null,
                                      bool useProxy = false) =>
                                      services.AddHttpClient(name, client =>
                                              {
                                                  client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                                                  configure?.Invoke(client);
                                              })
                                              .ConfigurePrimaryHttpMessageHandler(() =>
                                              {
                                                  var h = new SocketsHttpHandler { ConnectCallback = ssrfConnectCallback };
                                                  if (useProxy && webProxy is not null)
                                                  {
                                                      h.Proxy = webProxy;
                                                      h.UseProxy = true;
                                                  }

                                                  configureHandler?.Invoke(h);
                                                  return h;
                                              });

                                  // Returns a channel's BaseAddress URI from appConfig, or null if not set.
                                  // Channel classes store BaseAddress here so they only need relative paths.
                                  Uri? ChannelBaseUri(string channelKey, Func<ChannelConfig, string?> getUrl)
                                  {
                                      if (!appConfig.Channels.TryGetValue(channelKey, out var cfg))
                                      {
                                          return null;
                                      }

                                      var raw = getUrl(cfg);
                                      return string.IsNullOrWhiteSpace(raw)
                                          ? null
                                          : new Uri(raw.TrimEnd('/') + "/");
                                  }

                                  // LLM providers — longer timeout + Polly retry on 429 / 503.
                                  // No SSRF ConnectCallback: the base URL is admin-configured in
                                  // config.json, not user-controlled. Blocking private/loopback
                                  // IPs would break local providers (Ollama, LM Studio, vLLM, etc.).
                                  services.AddHttpClient("llm", client => { client.Timeout = TimeSpan.FromSeconds(120); })
                                          .ConfigurePrimaryHttpMessageHandler(() =>
                                          {
                                              var h = new SocketsHttpHandler();
                                              if (webProxy is not null)
                                              {
                                                  h.Proxy = webProxy;
                                                  h.UseProxy = true;
                                              }

                                              return h;
                                          })
                                          .AddResilienceHandler("llm-retry", static builder =>
                                          {
                                              builder.AddRetry(new HttpRetryStrategyOptions
                                              {
                                                  // ShouldHandle defaults already cover 429, 503, transient network
                                                  // errors, and Retry-After header is respected automatically.
                                                  MaxRetryAttempts = 3,
                                                  BackoffType = DelayBackoffType.Exponential,
                                                  UseJitter = true,
                                                  Delay = TimeSpan.FromSeconds(1),
                                                  MaxDelay = TimeSpan.FromSeconds(30)
                                              });
                                          });

                                  // Tool web requests — allow redirects for web fetch/search.
                                  AddSsrfSafeHttpClient("tools", useProxy: true, configureHandler: h =>
                                  {
                                      h.AllowAutoRedirect = true;
                                      h.MaxAutomaticRedirections = 5;
                                  });

                                  // Voice transcription (Groq / OpenAI Whisper / Azure / GCP).
                                  AddSsrfSafeHttpClient("transcription", timeoutSeconds: 60, useProxy: true);

                                  // Telegram — 35 s timeout (> 30 s long-poll).
                                  // BaseAddress = https://api.telegram.org/ so the channel uses
                                  // relative paths: "bot{token}/sendMessage", "file/bot{token}/..." etc.
                                  AddSsrfSafeHttpClient("telegram", timeoutSeconds: 35, configure: client =>
                                      client.BaseAddress = new Uri("https://api.telegram.org/"));

                                  // Slack — fixed Web API base; channel methods use relative paths
                                  // such as "chat.postMessage", "auth.test", "apps.connections.open".
                                  AddSsrfSafeHttpClient("slack", configure: client =>
                                      client.BaseAddress = new Uri("https://slack.com/api/"));

                                  // Matrix — 60 s (long-poll sync); BaseAddress from homeserver config.
                                  AddSsrfSafeHttpClient("matrix", timeoutSeconds: 60, configure: client =>
                                  {
                                      if (ChannelBaseUri("matrix", c => c.Homeserver) is { } uri)
                                      {
                                          client.BaseAddress = uri;
                                      }
                                  });

                                  // Signal — SSE stream + JSON-RPC; BaseAddress from bridge URL config.
                                  // Timeout only covers header receipt; SSE body reads are CancellationToken-bound.
                                  AddSsrfSafeHttpClient("signal", configure: client =>
                                  {
                                      if (ChannelBaseUri("signal", c => c.BridgeUrl) is { } uri)
                                      {
                                          client.BaseAddress = uri;
                                      }
                                  });

                                  // WhatsApp — REST bridge polling + send; BaseAddress from bridge URL config.
                                  AddSsrfSafeHttpClient("whatsapp", configure: client =>
                                  {
                                      if (ChannelBaseUri("whatsapp", c => c.BridgeUrl) is { } uri)
                                      {
                                          client.BaseAddress = uri;
                                      }
                                  });

                                  // Discord — dynamic CDN/attachment URLs; no fixed BaseAddress.
                                  AddSsrfSafeHttpClient("discord");

                                  // BlueBubbles — iMessage bridge; BaseAddress from bridge URL config.
                                  AddSsrfSafeHttpClient("bluebubbles", configure: client =>
                                  {
                                      if (ChannelBaseUri("bluebubbles", c => c.BridgeUrl) is { } uri)
                                      {
                                          client.BaseAddress = uri;
                                      }
                                  });

                                  // LINE — fixed Messaging API base; channel methods use relative paths.
                                  AddSsrfSafeHttpClient("line", configure: client =>
                                      client.BaseAddress = new Uri("https://api.line.me/"));

                                  // WeChat — bridge + WeCom webhook; BaseAddress for bridge path.
                                  // Absolute webhook calls (different host) override BaseAddress automatically.
                                  AddSsrfSafeHttpClient("wechat", configure: client =>
                                  {
                                      if (ChannelBaseUri("wechat", c => c.BridgeUrl) is { } uri)
                                      {
                                          client.BaseAddress = uri;
                                      }
                                  });

                                  // WeCom AI Bot — response_url based replies; no fixed BaseAddress.
                                  AddSsrfSafeHttpClient("wecom");

                                  // Lark/Feishu — domain determined by feishuDomain config.
                                  AddSsrfSafeHttpClient("lark", configure: client =>
                                  {
                                      if (!appConfig.Channels.TryGetValue("lark", out var larkCfg))
                                      {
                                          return;
                                      }

                                      var larkHost = string.Equals(larkCfg.FeishuDomain, "lark",
                                          StringComparison.OrdinalIgnoreCase)
                                          ? "https://open.larksuite.com/"
                                          : "https://open.feishu.cn/";
                                      client.BaseAddress = new Uri(larkHost);
                                  });

                                  // Mattermost — REST API; BaseAddress from server URL config.
                                  // WebSocket URL is derived separately (http→ws scheme swap) in the channel.
                                  AddSsrfSafeHttpClient("mattermost", configure: client =>
                                  {
                                      if (ChannelBaseUri("mattermost", c => c.MattermostUrl) is { } uri)
                                      {
                                          client.BaseAddress = uri;
                                      }
                                  });

                                  // QQ/OneBot — REST API for sending messages; no fixed BaseAddress (derived from WebSocket URL at runtime).
                                  AddSsrfSafeHttpClient("qq");

                                  // MCP remote servers (SSE / StreamableHTTP) — SSRF guard + proxy.
                                  AddSsrfSafeHttpClient("mcp", timeoutSeconds: 60, useProxy: true);

                                  // PinchTab browser sidecar — standard resilience, no SSRF ConnectCallback.
                                  // The base URL is admin-configured (default: http://localhost:9867) and
                                  // NOT user/LLM-controlled. Navigation URLs that the LLM supplies are
                                  // validated by SsrfGuard.CheckAsync() in PinchTabTool.NavigateAsync()
                                  // before being forwarded to the sidecar. Blocking private/loopback IPs
                                  // at connect time would prevent reaching the local PinchTab server.
                                  services.AddHttpClient("pinchtab", client => { client.Timeout = TimeSpan.FromSeconds(60); })
                                          .AddStandardResilienceHandler();

                                  // ── IOptions<T> registration ──────────────────────────────────
                                  services.AddSingleton<IConfiguration>(configuration);

                                  services.AddOptions<AppConfig>()
                                          .Bind(configuration)
                                          .ValidateDataAnnotations()
                                          .ValidateOnStart();

                                  // Decrypt enc2: secrets in-place so provider API keys and channel tokens
                                  // are readable at runtime when services consume IOptions<AppConfig>.
                                  services.PostConfigure<AppConfig>(ClawsharpConfiguration.DecryptSecrets);

                                  services.AddOptions<AgentDefaults>()
                                          .Bind(configuration.GetSection("agents:defaults"))
                                          .ValidateDataAnnotations()
                                          .ValidateOnStart();

                                  services.AddOptions<MemoryConfig>()
                                          .Bind(configuration.GetSection("memory"))
                                          .ValidateOnStart();

                                  services.AddOptions<ToolsConfig>()
                                          .Bind(configuration.GetSection("tools"))
                                          .ValidateOnStart();

                                  services.AddOptions<CostConfig>()
                                          .Bind(configuration.GetSection("cost"));

                                  // Source-generated validators
                                  services.AddSingleton<IValidateOptions<AgentDefaults>, ValidateAgentDefaults>();
                                  services.AddSingleton<IValidateOptions<AgentConfig>, ValidateAgentConfig>();
                                  services.AddSingleton<IValidateOptions<MemoryConfig>, ValidateMemoryConfig>();

                                  // Cross-cutting validation (provider in dict, conn strings, channel fields)
                                  services.AddSingleton<IValidateOptions<AppConfig>, AppConfigValidator>();

                                  // Embedding provider (optional, for semantic memory search)
                                  {
                                      var embCfg = appConfig.Memory.Embedding;
                                      if (embCfg is not null && !string.Equals(embCfg.Provider, "none", StringComparison.OrdinalIgnoreCase))
                                      {
                                          if (string.Equals(embCfg.Provider, "openai", StringComparison.OrdinalIgnoreCase))
                                          {
                                              services.AddSingleton<IEmbeddingProvider>(sp =>
                                                  new OpenAiEmbeddingProvider(
                                                      sp.GetRequiredService<IHttpClientFactory>(),
                                                      embCfg.ApiKey ?? "",
                                                      embCfg.Model ?? "text-embedding-3-small",
                                                      embCfg.BaseUrl ?? "https://api.openai.com/v1"));
                                          }
                                          else if (string.Equals(embCfg.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
                                          {
                                              services.AddSingleton<IEmbeddingProvider>(sp =>
                                                  new OllamaEmbeddingProvider(
                                                      sp.GetRequiredService<IHttpClientFactory>(),
                                                      embCfg.Model ?? "nomic-embed-text",
                                                      embCfg.BaseUrl ?? "http://localhost:11434",
                                                      appConfig.Memory.EmbeddingDimension));
                                          }
                                      }
                                  }

                                  // Memory backend (conditional on config)
                                  {
                                      var memBackend = MemoryBackend.TryFromValue(appConfig.Memory.Backend, out var mb)
                                          ? mb
                                          : MemoryBackend.Markdown;
                                      if (memBackend == MemoryBackend.Postgres)
                                      {
                                          var cs = appConfig.Memory.ConnectionString!;
                                          services.AddPooledDbContextFactory<PostgresMemoryContext>(o =>
                                              o.UseNpgsql(cs, npgsqlOpts => npgsqlOpts.UseVector())
                                               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                                          services.AddSingleton<IMemory>(sp =>
                                              new PostgresMemory(
                                                  sp.GetRequiredService<IDbContextFactory<PostgresMemoryContext>>(),
                                                  sp.GetRequiredService<ILogger<PostgresMemory>>(),
                                                  sp.GetService<IEmbeddingProvider>(),
                                                  sp.GetRequiredService<IOptions<MemoryConfig>>()));
                                      }
                                      else if (memBackend == MemoryBackend.MsSql)
                                      {
                                          var cs = appConfig.Memory.ConnectionString!;
                                          services.AddPooledDbContextFactory<MsSqlMemoryContext>(o =>
                                              o.UseSqlServer(cs)
                                               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                                          services.AddSingleton<IMemory>(sp =>
                                              new MsSqlMemory(
                                                  sp.GetRequiredService<IDbContextFactory<MsSqlMemoryContext>>(),
                                                  sp.GetRequiredService<ILogger<MsSqlMemory>>()));
                                      }
                                      else if (memBackend == MemoryBackend.Sqlite)
                                      {
                                          var sqliteDir = ConfigLoader.ExpandHome(appConfig.Memory.Dir);
                                          Directory.CreateDirectory(sqliteDir);
                                          var dbPath = Path.Combine(sqliteDir, "memory.db");
                                          var vecInterceptor = new SqliteVecConnectionInterceptor(
                                              logger: null); // Logger not yet available; interceptor logs on first load
                                          services.AddPooledDbContextFactory<SqliteMemoryContext>(o =>
                                              o.UseSqlite($"Data Source={dbPath}")
                                               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                                               .AddInterceptors(vecInterceptor));
                                          services.AddSingleton<IMemory>(sp =>
                                              new SqliteMemory(
                                                  sp.GetRequiredService<IDbContextFactory<SqliteMemoryContext>>(),
                                                  sp.GetRequiredService<ILogger<SqliteMemory>>(),
                                                  sp.GetService<IEmbeddingProvider>(),
                                                  sp.GetRequiredService<IOptions<MemoryConfig>>()));
                                      }
                                      else
                                      {
                                          var mdDir = ConfigLoader.ExpandHome(appConfig.Memory.Dir);
                                          Directory.CreateDirectory(mdDir);
                                          services.AddSingleton<IMemory>(new MarkdownMemory(mdDir));
                                      }
                                  }

                                  // Provider factory (runtime config-driven, keep manual)
                                  services.AddSingleton<IProvider>(sp =>
                                  {
                                      var initLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Init");
                                      var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                                      var devFlow = sp.GetRequiredService<GitHubDeviceFlow>();
                                      var opts = sp.GetRequiredService<IOptions<AppConfig>>().Value;
                                      var providerName = opts.Agents.Defaults.Provider;
                                      try
                                      {
                                          var p = ProviderFactory.Create(providerName, opts.Providers, httpFactory, devFlow);
                                          LogProviderInitialized(initLogger, p.Name, opts.Agents.Defaults.Model);
                                          return p;
                                      }
                                      catch (Exception ex)
                                      {
                                          LogProviderFallback(initLogger, ex);
                                          opts.Providers["ollama"] = new ProviderConfig
                                              { Type = "ollama", BaseUrl = "http://localhost:11434" };
                                          return ProviderFactory.Create("ollama", opts.Providers, httpFactory);
                                      }
                                  });

                                  // CronStore factory (runtime config-driven, keep manual)
                                  services.AddSingleton<ICronStore>(_ => CronStoreFactory.Create(appConfig));

                                  // Heartbeat service (conditional on config)
                                  if (appConfig.Agents.Defaults.Heartbeat is { Enabled: true })
                                  {
                                      services.AddHostedService<HeartbeatService>();
                                  }

                                  // Provider health check service (conditional on config)
                                  if (appConfig.Agents.Defaults.HealthCheck is { Enabled: true })
                                  {
                                      services.AddHostedService<ProviderHealthCheckService>();
                                  }

                                  // MCP servers (conditional — only when servers are configured)
                                  if (appConfig.McpServers is { Count: > 0 })
                                  {
                                      services.AddHostedService<McpHostedService>();
                                  }

                                  // Discord (conditional, Remora-specific — keep manual)
                                  if (discordEnabled)
                                  {
                                      services.AddSingleton(new DiscordChannelOptions(discordCfg!));
                                      services.AddSingleton<DiscordBotState>();
                                      services.AddSingleton<DiscordChannel>();
                                  }

                                  // Build IReadOnlyList<IChannel> from the IChannel service collection.
                                  // Resolved from GetServices<IChannel>() — NOT GetServices<IHostedService>()
                                  // to avoid circular dependency (AgentLoopService → IReadOnlyList<IChannel>
                                  // → GetServices<IHostedService>() → AgentLoopService → ∞).
                                  services.AddSingleton<IReadOnlyList<IChannel>>(sp =>
                                  {
                                      var opts = sp.GetRequiredService<IOptions<AppConfig>>().Value;
                                      var enabledKeys = opts.Channels
                                                            .Where(kv => kv.Value.Enabled)
                                                            .Select(kv => kv.Key)
                                                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                                      // Always include CLI if it's the fallback (no other channels enabled and Discord is off)
                                      var anyNonCliEnabled = opts.Channels.Any(kv =>
                                          !string.Equals(kv.Key, ChannelName.Cli.Value, StringComparison.OrdinalIgnoreCase) &&
                                          kv.Value.Enabled);
                                      if (!anyNonCliEnabled && !discordEnabled)
                                      {
                                          enabledKeys.Add(ChannelName.Cli.Value);
                                      }

                                      var list = sp.GetServices<IChannel>()
                                                   .Where(c => enabledKeys.Contains(c.Name.Value))
                                                   .ToList();

                                      if (discordEnabled)
                                      {
                                          list.Add(sp.GetRequiredService<DiscordChannel>());
                                      }

                                      return list;
                                  });

                                  // ── Immediate.Handlers (VSA/CQRS) ────────────────────────
                                  services.AddclawsharpBehaviors();
                                  services.AddclawsharpHandlers(ServiceLifetime.Singleton);
                                  services.AddSingleton<AgentHandlers>();

                                  // ── Singletons ──────────────────────────────────────────────
                                  services.AddSingleton<IMessageBus, InMemoryMessageBus>();
                                  services.AddSingleton<IToolRegistry, ToolRegistry>();
                                  services.AddSingleton<AgentLoop>();
                                  services.AddSingleton<SessionManager>();
                                  services.AddSingleton<RateLimiter>();
                                  services.AddSingleton<CompactionService>();
                                  services.AddSingleton<CooldownTracker>();
                                  services.AddSingleton<FallbackChain>();
                                  services.AddSingleton<VoiceTranscriptionService>();
                                  services.AddSingleton<CostTracker>();
                                  services.AddSingleton<CostStorage>();
                                  services.AddSingleton<AuditLogger>();
                                  services.AddSingleton<SecretStore>();
                                  services.AddSingleton<PairingStore>();
                                  services.AddSingleton<WebPairingService>();
                                  services.AddSingleton<SandboxProbe>();
                                  services.AddSingleton<ApprovedSendersStore>();
                                  services.AddSingleton<GoalStorage>();
                                  services.AddSingleton<FactExtractor>();
                                  services.AddSingleton<BrowserSessionManager>();
                                  services.AddSingleton<PinchTabSessionManager>();
                                  services.AddSingleton<GitHubDeviceFlow>();

                                  // Interaction analytics — storage backend (JSONL file, SQLite, Postgres, MS SQL)
                                  {
                                      var analyticsBackend = appConfig.Analytics?.Backend ?? "jsonl";
                                      if (string.Equals(analyticsBackend, "postgres", StringComparison.OrdinalIgnoreCase))
                                      {
                                          var cs = appConfig.Analytics?.ConnectionString
                                                   ?? appConfig.Memory.ConnectionString
                                                   ?? throw new InvalidOperationException(
                                                       "Analytics backend 'postgres' requires a connection string. " +
                                                       "Set analytics.connectionString or memory.connectionString in config.");
                                          services.AddPooledDbContextFactory<PostgresAnalyticsContext>(o =>
                                              o.UseNpgsql(cs)
                                               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                                          services.AddSingleton<IInteractionStore>(sp =>
                                              new EfInteractionStore<PostgresAnalyticsContext>(
                                                  sp.GetRequiredService<IDbContextFactory<PostgresAnalyticsContext>>(),
                                                  sp.GetRequiredService<ILogger<EfInteractionStore<PostgresAnalyticsContext>>>()));
                                      }
                                      else if (string.Equals(analyticsBackend, "mssql", StringComparison.OrdinalIgnoreCase))
                                      {
                                          var cs = appConfig.Analytics?.ConnectionString
                                                   ?? appConfig.Memory.ConnectionString
                                                   ?? throw new InvalidOperationException(
                                                       "Analytics backend 'mssql' requires a connection string. " +
                                                       "Set analytics.connectionString or memory.connectionString in config.");
                                          services.AddPooledDbContextFactory<MsSqlAnalyticsContext>(o =>
                                              o.UseSqlServer(cs)
                                               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                                          services.AddSingleton<IInteractionStore>(sp =>
                                              new EfInteractionStore<MsSqlAnalyticsContext>(
                                                  sp.GetRequiredService<IDbContextFactory<MsSqlAnalyticsContext>>(),
                                                  sp.GetRequiredService<ILogger<EfInteractionStore<MsSqlAnalyticsContext>>>()));
                                      }
                                      else if (string.Equals(analyticsBackend, "sqlite", StringComparison.OrdinalIgnoreCase))
                                      {
                                          var sqliteDir = ConfigLoader.ExpandHome(appConfig.Analytics?.Dir ?? "~/.clawsharp");
                                          Directory.CreateDirectory(sqliteDir);
                                          var dbPath = Path.Combine(sqliteDir, "analytics.db");
                                          services.AddPooledDbContextFactory<SqliteAnalyticsContext>(o =>
                                              o.UseSqlite($"Data Source={dbPath}")
                                               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                                          services.AddSingleton<IInteractionStore>(sp =>
                                              new EfInteractionStore<SqliteAnalyticsContext>(
                                                  sp.GetRequiredService<IDbContextFactory<SqliteAnalyticsContext>>(),
                                                  sp.GetRequiredService<ILogger<EfInteractionStore<SqliteAnalyticsContext>>>()));
                                      }
                                      else
                                      {
                                          // Default: JSONL file storage
                                          services.AddSingleton<IInteractionStore, InteractionStorage>();
                                      }
                                  }
                                  services.AddSingleton<InteractionTracker>(sp =>
                                      new InteractionTracker(
                                          sp.GetRequiredService<IInteractionStore>(),
                                          sp.GetRequiredService<IMemory>(),
                                          sp.GetRequiredService<ILogger<InteractionTracker>>(),
                                          appConfig.Analytics?.StoreInMemory ?? false));

                                  // CronService needs singleton + hosted service so it can be
                                  // resolved by type AND participate in the host lifecycle.
                                  services.AddSingleton<CronService>();
                                  services.AddSingleton<IHostedService, CronService>(sp => sp.GetRequiredService<CronService>());

                                  // ── Channels ──────────────────────────────────────────────
                                  // Only register channels that are enabled in config to avoid
                                  // unnecessary construction, DI resolution, and lifecycle calls.
                                  // CLI is always registered as the interactive fallback.
                                  // Channels use triple-registration (see AddChannel<T> docs).

                                  // Local helper — true if the channel key exists and is enabled.
                                  bool IsChannelEnabled(string key) =>
                                      appConfig.Channels.TryGetValue(key, out var cfg) && cfg.Enabled;

                                  AddChannel<CliChannel>(services);
                                  if (IsChannelEnabled("web")) AddChannel<WebChannel>(services);
                                  if (IsChannelEnabled("telegram")) AddChannel<TelegramChannel>(services);
                                  if (IsChannelEnabled("slack")) AddChannel<SlackChannel>(services);
                                  if (IsChannelEnabled("matrix")) AddChannel<MatrixChannel>(services);
                                  if (IsChannelEnabled("email")) AddChannel<EmailChannel>(services);
                                  if (IsChannelEnabled("irc")) AddChannel<IrcChannel>(services);
                                  if (IsChannelEnabled("mattermost")) AddChannel<MattermostChannel>(services);
                                  if (IsChannelEnabled("nostr")) AddChannel<NostrChannel>(services);
                                  if (IsChannelEnabled("qq")) AddChannel<QqChannel>(services);
                                  if (IsChannelEnabled("signal")) AddChannel<SignalChannel>(services);
                                  if (IsChannelEnabled("whatsapp")) AddChannel<WhatsAppChannel>(services);
                                  if (IsChannelEnabled("wechat")) AddChannel<WeChatChannel>(services);
                                  if (IsChannelEnabled("bluebubbles")) AddChannel<BlueBubblesChannel>(services);
                                  if (IsChannelEnabled("line")) AddChannel<LineChannel>(services);
                                  if (IsChannelEnabled("lark")) AddChannel<LarkChannel>(services);
                                  if (IsChannelEnabled("wecom")) AddChannel<WeComChannel>(services);

                                  // ── Non-channel hosted services ────────────────────────────
                                  services.AddHostedService<ViteDevOrchestrator>();
                                  services.AddHostedService<AgentLoopService>();
                                  services.AddHostedService<GatewayIpcService>();

                                  // Memory decay (conditional — only when decay is enabled)
                                  if (appConfig.Memory.Decay is { Enabled: true, TtlDays: > 0 })
                                  {
                                      services.AddHostedService<MemoryDecayService>();
                                  }
                              });

        if (discordEnabled)
        {
            hostBuilder
                .AddDiscordService(_ => discordCfg!.Token!)
                .ConfigureServices((_, services) =>
                {
                    services.Configure<DiscordGatewayClientOptions>(o =>
                        o.Intents |= GatewayIntents.Guilds
                                     | GatewayIntents.DirectMessages
                                     | GatewayIntents.GuildMessages
                                     | GatewayIntents.MessageContents);

                    services.AddResponder<DiscordReadyResponder>();
                    services.AddResponder<DiscordMessageResponder>();
                });
        }

        await hostBuilder.RunConsoleAsync(ct);
    }

    /// <summary>
    ///     Registers a channel as a concrete singleton, as <see cref="IHostedService"/>
    ///     (for host lifecycle management), and as <see cref="IChannel"/> (for discovery
    ///     by <see cref="IReadOnlyList{IChannel}"/>). This triple-registration avoids the
    ///     circular dependency that <c>AddHostedService</c> causes when
    ///     <c>IReadOnlyList&lt;IChannel&gt;</c> resolves via <c>GetServices&lt;IHostedService&gt;</c>.
    /// </summary>
    private static void AddChannel<T>(IServiceCollection services) where T : class, IChannel, IHostedService
    {
        services.AddSingleton<T>();
        services.AddSingleton<IHostedService, T>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IChannel, T>(sp => sp.GetRequiredService<T>());
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Provider: {ProviderName} | Model: {Model}")]
    private static partial void LogProviderInitialized(ILogger logger, string providerName, string model);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Provider error, falling back to Ollama defaults")]
    private static partial void LogProviderFallback(ILogger logger, Exception exception);
}