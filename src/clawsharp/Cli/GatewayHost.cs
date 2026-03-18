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
using Clawsharp.Core.Resilience;
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
using Microsoft.Extensions.Logging.Abstractions;
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
using System.Net.Http;

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
        var configuration = ClawsharpConfiguration.Build();

        var appConfig = configuration.Get<AppConfig>() ?? new AppConfig();
        ClawsharpConfiguration.DecryptSecrets(appConfig);

        PromptGuard.Configure(appConfig.Security?.PromptGuard);

        if (!ValidateConfiguration(appConfig))
        {
            return;
        }

        ApplyLandlockSandbox(appConfig);

        appConfig.Channels.TryGetValue("discord", out var discordCfg);
        var discordEnabled = discordCfg is { Enabled: true, Token: not null };

        var hostBuilder = Host.CreateDefaultBuilder(Array.Empty<string>())
                              .ConfigureLogging(ConfigureLogging)
                              .ConfigureServices((_, services) =>
                              {
                                  var ssrfConnectCallback = SsrfGuard.CreateConnectCallback();
                                  var webProxy = CreateProxy(appConfig);

                                  ConfigureHostOptions(services);
                                  AddLlmHttpClient(services, appConfig, webProxy);
                                  AddToolAndTranscriptionHttpClients(services, ssrfConnectCallback, webProxy);
                                  AddChannelHttpClients(services, appConfig, ssrfConnectCallback, webProxy);
                                  services.AddChannelResiliencePipelines(appConfig.Channels);
                                  RegisterOptions(services, configuration, appConfig);
                                  RegisterEmbeddingProvider(services, appConfig);
                                  RegisterMemoryBackend(services, appConfig);
                                  RegisterProviderFactory(services, appConfig);
                                  RegisterConditionalHostedServices(services, appConfig);
                                  RegisterDiscordServices(services, discordEnabled, discordCfg);
                                  RegisterChannelList(services, appConfig, discordEnabled);
                                  RegisterHandlers(services);
                                  RegisterCoreSingletons(services);
                                  RegisterAnalytics(services, appConfig);
                                  RegisterCronService(services);
                                  RegisterChannels(services, appConfig);
                                  RegisterNonChannelHostedServices(services, appConfig);
                              });

        if (discordEnabled)
        {
            ConfigureDiscord(hostBuilder, discordCfg!);
        }

        await hostBuilder.RunConsoleAsync(ct);
    }

    private static bool ValidateConfiguration(AppConfig appConfig)
    {
        var validationErrors = ConfigValidator.Validate(appConfig);
        if (validationErrors.Count == 0)
        {
            return true;
        }

        foreach (var err in validationErrors)
        {
            AnsiConsole.MarkupLine($"[red]Config validation error:[/] {Markup.Escape(err)}");
        }

        Environment.ExitCode = 1;
        return false;
    }

    private static void ApplyLandlockSandbox(AppConfig appConfig)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var landlockLoggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        var landlockLogger = landlockLoggerFactory.CreateLogger("Landlock");
        var shellEnabled = appConfig.Tools.ShellEnabled;
        LandlockSandbox.Apply(appConfig.Security?.Landlock ?? new LandlockConfig(), landlockLogger, shellEnabled);
    }

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddDebug();
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        logging.AddFilter("Remora", LogLevel.Warning);
        logging.SetMinimumLevel(LogLevel.Information);
    }

    private static void ConfigureHostOptions(IServiceCollection services)
    {
        services.Configure<HostOptions>(hostOpts =>
        {
            hostOpts.ShutdownTimeout = TimeSpan.FromSeconds(10);
            // Prevent a single channel's unhandled exception from terminating the entire gateway.
            // Each channel's ExecuteAsync already has its own exception handling, but this is a
            // safety net so any uncaught exception logs and isolates rather than crashing the host.
            hostOpts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });
    }

    private static System.Net.WebProxy? CreateProxy(AppConfig appConfig)
    {
        var proxyUrl = appConfig.HttpRequest?.Proxy
                       ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                       ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                       ?? Environment.GetEnvironmentVariable("ALL_PROXY");

        if (proxyUrl is null)
        {
            return null;
        }

        return new System.Net.WebProxy(proxyUrl);
    }

    private static IHttpClientBuilder AddSsrfSafeHttpClient(
        IServiceCollection services,
        Func<SocketsHttpHandler, SocketsHttpHandler> createHandler,
        string name,
        int timeoutSeconds = 30,
        Action<HttpClient>? configure = null,
        Action<SocketsHttpHandler>? configureHandler = null) =>
        services.AddHttpClient(name, client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    configure?.Invoke(client);
                })
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    var h = createHandler(new SocketsHttpHandler());
                    configureHandler?.Invoke(h);
                    return h;
                });

    private static Func<SocketsHttpHandler, SocketsHttpHandler> CreateHandlerFactory(
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ssrfConnectCallback,
        System.Net.WebProxy? webProxy,
        bool useProxy)
    {
        return h =>
        {
            h.ConnectCallback = ssrfConnectCallback;
            if (useProxy && webProxy is not null)
            {
                h.Proxy = webProxy;
                h.UseProxy = true;
            }

            return h;
        };
    }

    private static Uri? ChannelBaseUri(AppConfig appConfig, string channelKey, Func<ChannelConfig, string?> getUrl)
    {
        if (!appConfig.Channels.TryGetValue(channelKey, out var cfg))
        {
            return null;
        }

        var raw = getUrl(cfg);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return new Uri(raw.TrimEnd('/') + "/");
    }

    private static void AddLlmHttpClient(
        IServiceCollection services,
        AppConfig appConfig,
        System.Net.WebProxy? webProxy)
    {
        var resilience = appConfig.Agents.Defaults.Resilience;
        var retryConfig = resilience?.Retry ?? new RetryPolicyConfig();
        var requestTimeout = resilience?.RequestTimeout ?? TimeSpan.FromSeconds(120);

        services.AddHttpClient("llm", client => { client.Timeout = requestTimeout; })
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
                .AddResilienceHandler("llm-retry", builder =>
                {
                    const int hardCap = 50;
                    var maxAttempts = retryConfig.MaxRetryAttempts < 0
                        ? hardCap
                        : retryConfig.MaxRetryAttempts;

                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = maxAttempts,
                        BackoffType = retryConfig.BackoffType.ToLowerInvariant() switch
                        {
                            "linear" => DelayBackoffType.Linear,
                            "constant" => DelayBackoffType.Constant,
                            _ => DelayBackoffType.Exponential,
                        },
                        UseJitter = retryConfig.UseJitter,
                        Delay = retryConfig.Delay,
                        MaxDelay = retryConfig.MaxDelay,
                    });

                    if (resilience?.CircuitBreaker is { Enabled: true } cb)
                    {
                        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                        {
                            FailureRatio = cb.FailureRatio,
                            MinimumThroughput = cb.MinimumThroughput,
                            BreakDuration = cb.BreakDuration,
                            SamplingDuration = cb.SamplingDuration,
                        });
                    }
                });
    }

    private static void AddToolAndTranscriptionHttpClients(
        IServiceCollection services,
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ssrfConnectCallback,
        System.Net.WebProxy? webProxy)
    {
        var proxyHandler = CreateHandlerFactory(ssrfConnectCallback, webProxy, useProxy: true);

        AddSsrfSafeHttpClient(services, proxyHandler, "tools",
            configureHandler: h =>
            {
                h.AllowAutoRedirect = true;
                h.MaxAutomaticRedirections = 5;
            });

        AddSsrfSafeHttpClient(services, proxyHandler, "transcription", timeoutSeconds: 60);

        AddSsrfSafeHttpClient(services, proxyHandler, "mcp", timeoutSeconds: 60);

        // PinchTab browser sidecar — standard resilience, no SSRF ConnectCallback.
        // The base URL is admin-configured (default: http://localhost:9867) and
        // NOT user/LLM-controlled. Navigation URLs that the LLM supplies are
        // validated by SsrfGuard.CheckAsync() in PinchTabTool.NavigateAsync()
        // before being forwarded to the sidecar. Blocking private/loopback IPs
        // at connect time would prevent reaching the local PinchTab server.
        services.AddHttpClient("pinchtab", client => { client.Timeout = TimeSpan.FromSeconds(60); })
                .AddStandardResilienceHandler();
    }

    private static void AddChannelHttpClients(
        IServiceCollection services,
        AppConfig appConfig,
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ssrfConnectCallback,
        System.Net.WebProxy? webProxy)
    {
        var noProxyHandler = CreateHandlerFactory(ssrfConnectCallback, webProxy, useProxy: false);

        // Telegram — 35 s timeout (> 30 s long-poll).
        AddSsrfSafeHttpClient(services, noProxyHandler, "telegram", timeoutSeconds: 35, configure: client =>
            client.BaseAddress = new Uri(ClawsharpConstants.TelegramBaseUrl));

        // Slack — fixed Web API base.
        AddSsrfSafeHttpClient(services, noProxyHandler, "slack", configure: client =>
            client.BaseAddress = new Uri(ClawsharpConstants.SlackBaseUrl));

        // Matrix — 60 s (long-poll sync).
        AddSsrfSafeHttpClient(services, noProxyHandler, "matrix", timeoutSeconds: 60, configure: client =>
        {
            if (ChannelBaseUri(appConfig, "matrix", c => c.Homeserver) is { } uri)
            {
                client.BaseAddress = uri;
            }
        });

        // Signal — SSE stream + JSON-RPC.
        AddSsrfSafeHttpClient(services, noProxyHandler, "signal", timeoutSeconds: 30, configure: client =>
        {
            if (ChannelBaseUri(appConfig, "signal", c => c.BridgeUrl) is { } uri)
            {
                client.BaseAddress = uri;
            }
        });

        // WhatsApp — REST bridge polling + send.
        AddSsrfSafeHttpClient(services, noProxyHandler, "whatsapp", timeoutSeconds: 30, configure: client =>
        {
            if (ChannelBaseUri(appConfig, "whatsapp", c => c.BridgeUrl) is { } uri)
            {
                client.BaseAddress = uri;
            }
        });

        // Discord — dynamic CDN/attachment URLs; no fixed BaseAddress.
        AddSsrfSafeHttpClient(services, noProxyHandler, "discord", timeoutSeconds: 30);

        // BlueBubbles — iMessage bridge.
        AddSsrfSafeHttpClient(services, noProxyHandler, "bluebubbles", configure: client =>
        {
            if (ChannelBaseUri(appConfig, "bluebubbles", c => c.BridgeUrl) is { } uri)
            {
                client.BaseAddress = uri;
            }
        });

        // LINE — fixed Messaging API base.
        AddSsrfSafeHttpClient(services, noProxyHandler, "line", configure: client =>
            client.BaseAddress = new Uri(ClawsharpConstants.LineBaseUrl));

        // WeChat — bridge + WeCom webhook.
        AddSsrfSafeHttpClient(services, noProxyHandler, "wechat", configure: client =>
        {
            if (ChannelBaseUri(appConfig, "wechat", c => c.BridgeUrl) is { } uri)
            {
                client.BaseAddress = uri;
            }
        });

        // WeCom AI Bot — response_url based replies; no fixed BaseAddress.
        AddSsrfSafeHttpClient(services, noProxyHandler, "wecom");

        // Lark/Feishu — domain determined by feishuDomain config.
        AddSsrfSafeHttpClient(services, noProxyHandler, "lark", configure: client =>
        {
            if (!appConfig.Channels.TryGetValue("lark", out var larkCfg))
            {
                return;
            }

            string larkHost;
            if (string.Equals(larkCfg.FeishuDomain, FeishuDomain.Lark, StringComparison.OrdinalIgnoreCase))
            {
                larkHost = ClawsharpConstants.LarkBaseUrl;
            }
            else
            {
                larkHost = ClawsharpConstants.FeishuBaseUrl;
            }

            client.BaseAddress = new Uri(larkHost);
        });

        // Mattermost — REST API.
        AddSsrfSafeHttpClient(services, noProxyHandler, "mattermost", timeoutSeconds: 30, configure: client =>
        {
            if (ChannelBaseUri(appConfig, "mattermost", c => c.MattermostUrl) is { } uri)
            {
                client.BaseAddress = uri;
            }
        });

        // QQ/OneBot — REST API for sending messages; no fixed BaseAddress.
        AddSsrfSafeHttpClient(services, noProxyHandler, "qq");
    }

    private static void RegisterOptions(
        IServiceCollection services,
        IConfiguration configuration,
        AppConfig appConfig)
    {
        services.AddSingleton<IConfiguration>(configuration);

        services.AddOptions<AppConfig>()
                .Bind(configuration)
                .ValidateDataAnnotations()
                .ValidateOnStart();

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

        services.AddSingleton<IValidateOptions<AgentDefaults>, ValidateAgentDefaults>();
        services.AddSingleton<IValidateOptions<AgentConfig>, ValidateAgentConfig>();
        services.AddSingleton<IValidateOptions<MemoryConfig>, ValidateMemoryConfig>();

        services.AddSingleton<IValidateOptions<AppConfig>, AppConfigValidator>();
    }

    private static void RegisterEmbeddingProvider(IServiceCollection services, AppConfig appConfig)
    {
        var embCfg = appConfig.Memory.Embedding;
        if (embCfg is null || string.Equals(embCfg.Provider, EmbeddingProvider.None, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(embCfg.Provider, EmbeddingProvider.OpenAi, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmbeddingProvider>(sp =>
                new OpenAiEmbeddingProvider(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    embCfg.ApiKey ?? "",
                    embCfg.Model ?? "text-embedding-3-small",
                    embCfg.BaseUrl ?? ClawsharpConstants.OpenAiDefaultBaseUrl));
        }
        else if (string.Equals(embCfg.Provider, EmbeddingProvider.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmbeddingProvider>(sp =>
                new OllamaEmbeddingProvider(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    embCfg.Model ?? "nomic-embed-text",
                    embCfg.BaseUrl ?? ClawsharpConstants.OllamaDefaultBaseUrl,
                    appConfig.Memory.EmbeddingDimension));
        }
    }

    [RequiresUnreferencedCode("EF Core query compilation requires unreferenced code")]
    [RequiresDynamicCode("EF Core query compilation requires dynamic code generation")]
    private static void RegisterMemoryBackend(IServiceCollection services, AppConfig appConfig)
    {
        MemoryBackend memBackend;
        if (MemoryBackend.TryFromValue(appConfig.Memory.Backend, out var mb))
        {
            memBackend = mb;
        }
        else
        {
            memBackend = MemoryBackend.Markdown;
        }

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
                NullLogger.Instance);
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

    private static void RegisterProviderFactory(IServiceCollection services, AppConfig appConfig)
    {
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
                    { Type = "ollama", BaseUrl = ClawsharpConstants.OllamaDefaultBaseUrl };
                return ProviderFactory.Create("ollama", opts.Providers, httpFactory);
            }
        });

        services.AddSingleton<ICronStore>(_ => CronStoreFactory.Create(appConfig));
    }

    private static void RegisterConditionalHostedServices(IServiceCollection services, AppConfig appConfig)
    {
        if (appConfig.Agents.Defaults.Heartbeat is { Enabled: true })
        {
            services.AddHostedService<HeartbeatService>();
        }

        if (appConfig.Agents.Defaults.HealthCheck is { Enabled: true })
        {
            services.AddHostedService<ProviderHealthCheckService>();
        }

        if (appConfig.McpServers is { Count: > 0 })
        {
            services.AddHostedService<McpHostedService>();
        }
    }

    private static void RegisterDiscordServices(
        IServiceCollection services,
        bool discordEnabled,
        ChannelConfig? discordCfg)
    {
        if (!discordEnabled)
        {
            return;
        }

        services.AddSingleton(new DiscordChannelOptions(discordCfg!));
        services.AddSingleton<DiscordBotState>();
        services.AddSingleton<DiscordChannel>();
    }

    private static void RegisterChannelList(
        IServiceCollection services,
        AppConfig appConfig,
        bool discordEnabled)
    {
        services.AddSingleton<IReadOnlyList<IChannel>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var enabledKeys = opts.Channels
                                  .Where(kv => kv.Value.Enabled)
                                  .Select(kv => kv.Key)
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
    }

    private static void RegisterHandlers(IServiceCollection services)
    {
        services.AddclawsharpBehaviors();
        services.AddclawsharpHandlers(ServiceLifetime.Singleton);
        services.AddSingleton<AgentHandlers>();
    }

    private static void RegisterCoreSingletons(IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, InMemoryMessageBus>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<AgentLoop>();
        services.AddSingleton<SessionStore>();
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
        services.AddSingleton<BrowserSessionCache>();
        services.AddSingleton<PinchTabSessionManager>();
        services.AddSingleton<GitHubDeviceFlow>();
    }

    [RequiresUnreferencedCode("EF Core query compilation requires unreferenced code")]
    [RequiresDynamicCode("EF Core query compilation requires dynamic code generation")]
    private static void RegisterAnalytics(IServiceCollection services, AppConfig appConfig)
    {
        var analyticsBackend = appConfig.Analytics?.Backend ?? "jsonl";
        if (string.Equals(analyticsBackend, AnalyticsBackend.Postgres, StringComparison.OrdinalIgnoreCase))
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
        else if (string.Equals(analyticsBackend, AnalyticsBackend.MsSql, StringComparison.OrdinalIgnoreCase))
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
        else if (string.Equals(analyticsBackend, AnalyticsBackend.Sqlite, StringComparison.OrdinalIgnoreCase))
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
            services.AddSingleton<IInteractionStore, InteractionStorage>();
        }

        services.AddSingleton<InteractionTracker>(sp =>
            new InteractionTracker(
                sp.GetRequiredService<IInteractionStore>(),
                sp.GetRequiredService<IMemory>(),
                sp.GetRequiredService<ILogger<InteractionTracker>>(),
                appConfig.Analytics?.StoreInMemory ?? false));
    }

    private static void RegisterCronService(IServiceCollection services)
    {
        services.AddSingleton<CronService>();
        services.AddSingleton<IHostedService, CronService>(sp => sp.GetRequiredService<CronService>());
    }

    private static void RegisterChannels(IServiceCollection services, AppConfig appConfig)
    {
        bool IsChannelEnabled(string key) =>
            appConfig.Channels.TryGetValue(key, out var cfg) && cfg.Enabled;

        AddChannel<CliChannel>(services);

        if (IsChannelEnabled("web"))
        {
            AddChannel<WebChannel>(services);
        }

        if (IsChannelEnabled("telegram"))
        {
            AddChannel<TelegramChannel>(services);
        }

        if (IsChannelEnabled("slack"))
        {
            AddChannel<SlackChannel>(services);
        }

        if (IsChannelEnabled("matrix"))
        {
            AddChannel<MatrixChannel>(services);
        }

        if (IsChannelEnabled("email"))
        {
            AddChannel<EmailChannel>(services);
        }

        if (IsChannelEnabled("irc"))
        {
            AddChannel<IrcChannel>(services);
        }

        if (IsChannelEnabled("mattermost"))
        {
            AddChannel<MattermostChannel>(services);
        }

        if (IsChannelEnabled("nostr"))
        {
            AddChannel<NostrChannel>(services);
        }

        if (IsChannelEnabled("qq"))
        {
            AddChannel<QqChannel>(services);
        }

        if (IsChannelEnabled("signal"))
        {
            AddChannel<SignalChannel>(services);
        }

        if (IsChannelEnabled("whatsapp"))
        {
            AddChannel<WhatsAppChannel>(services);
        }

        if (IsChannelEnabled("wechat"))
        {
            AddChannel<WeChatChannel>(services);
        }

        if (IsChannelEnabled("bluebubbles"))
        {
            AddChannel<BlueBubblesChannel>(services);
        }

        if (IsChannelEnabled("line"))
        {
            AddChannel<LineChannel>(services);
        }

        if (IsChannelEnabled("lark"))
        {
            AddChannel<LarkChannel>(services);
        }

        if (IsChannelEnabled("wecom"))
        {
            AddChannel<WeComChannel>(services);
        }
    }

    private static void RegisterNonChannelHostedServices(IServiceCollection services, AppConfig appConfig)
    {
        services.AddHostedService<ViteDevOrchestrator>();
        services.AddHostedService<AgentLoopService>();
        services.AddHostedService<GatewayIpcService>();

        if (appConfig.Memory.Decay is { Enabled: true, TtlDays: > 0 })
        {
            services.AddHostedService<MemoryDecayService>();
        }
    }

    private static void ConfigureDiscord(IHostBuilder hostBuilder, ChannelConfig discordCfg)
    {
        hostBuilder
            .AddDiscordService(_ => discordCfg.Token!)
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
