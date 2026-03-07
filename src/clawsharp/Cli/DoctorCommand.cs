using System.ComponentModel;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Providers;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Memory;

namespace Clawsharp.Cli;

/// <summary>
/// Runs health checks on the configuration.
/// Exit 0 = all clear, 1 = warnings only (degraded but functional), 2 = one or more failures.
/// </summary>
[UsedImplicitly]
public sealed class DoctorCommand : AsyncCommand<DoctorCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--deep")]
        [Description("Run live connectivity checks (API calls, DB connections)")]
        [DefaultValue(false)]
        public bool Deep { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var failures = 0;
        var warnings = 0;

        AnsiConsole.MarkupLine("[bold]clawsharp doctor[/]");
        AnsiConsole.WriteLine();

        // Provider
        var providerName = config.Agents.Defaults.Provider;
        config.Providers.TryGetValue(providerName, out var providerCfg);
        if (providerCfg is null)
        {
            Fail($"Provider '{providerName}' not found in config");
            failures++;
        }
        else
        {
            var credentialErrors = new List<string>();
            ConfigValidator.ValidateProviderCredentials(credentialErrors, providerName, providerCfg);
            if (credentialErrors.Count > 0)
            {
                foreach (var err in credentialErrors)
                {
                    Warn($"Provider '{providerName}' ({providerCfg.Type}): {err}");
                }

                warnings += credentialErrors.Count;
            }
            else
            {
                Ok($"Provider '{providerName}' ({providerCfg.Type}) configured");
            }
        }

        // Memory
        var backend = config.Memory.Backend;
        var backendKnown = MemoryBackend.TryFromValue(backend, out var memBackend);
        if (backendKnown && (memBackend == MemoryBackend.Markdown || memBackend == MemoryBackend.Sqlite))
        {
            var dir = ConfigLoader.ExpandHome(config.Memory.Dir);
            if (Directory.Exists(dir))
            {
                Ok($"Memory dir exists: {dir}");
            }
            else
            {
                Warn($"Memory dir will be created on first run: {dir}");
                warnings++;
            }
        }
        else if (backendKnown && (memBackend == MemoryBackend.Postgres || memBackend == MemoryBackend.MsSql))
        {
            if (string.IsNullOrEmpty(config.Memory.ConnectionString))
            {
                Fail($"Memory backend '{backend}' requires memory.connectionString");
                failures++;
            }
            else
            {
                Ok($"Memory backend '{backend}' connection string set");
            }
        }

        // Workspace
        var workspace = ConfigLoader.ExpandHome(config.Tools.Workspace);
        if (Directory.Exists(workspace))
        {
            Ok($"Workspace exists: {workspace}");
        }
        else
        {
            Warn($"Workspace will be created on first run: {workspace}");
            warnings++;
        }

        // Channels
        var enabledChannels = config.Channels
                                    .Where(kv => kv.Value.Enabled)
                                    .Select(kv => kv.Key)
                                    .ToList();

        if (enabledChannels.Count == 0)
        {
            Warn("No channels enabled — CLI will be used as fallback");
            warnings++;
        }
        else
        {
            Ok($"Enabled channels: {string.Join(", ", enabledChannels)}");
        }

        // Heartbeat
        if (config.Agents.Defaults.Heartbeat is { Enabled: true } heartbeat)
        {
            var heartbeatChannel = heartbeat.Channel;
            if (config.Channels.TryGetValue(heartbeatChannel, out var hbChannelCfg) && hbChannelCfg.Enabled)
            {
                Ok($"Heartbeat targets enabled channel '{heartbeatChannel}'");
            }
            else if (config.Channels.ContainsKey(heartbeatChannel))
            {
                Warn($"Heartbeat targets channel '{heartbeatChannel}' which is configured but not enabled");
                warnings++;
            }
            else
            {
                // CLI is always implicitly available even without explicit config
                if (!heartbeatChannel.Equals("cli", StringComparison.OrdinalIgnoreCase))
                {
                    Warn($"Heartbeat targets channel '{heartbeatChannel}' which is not configured");
                    warnings++;
                }
                else
                {
                    Ok("Heartbeat targets CLI channel (always available)");
                }
            }
        }

        // Deep checks
        if (settings.Deep)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Deep checks:[/]");

            // Provider API ping
            try
            {
                var svcs = new ServiceCollection();
                svcs.AddHttpClient("llm", c => c.Timeout = TimeSpan.FromSeconds(30));
                using var sp = svcs.BuildServiceProvider();
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                var p = ProviderFactory.Create(config.Agents.Defaults.Provider, config.Providers, httpFactory);
                var pingReq = new ChatRequest(
                    Model: config.Agents.Defaults.Model,
                    Messages: [new ChatMessage(MessageRole.User, "ping")],
                    Temperature: 0,
                    MaxTokens: 1);
                await p.ChatAsync(pingReq, cancellationToken);
                Ok($"Provider '{config.Agents.Defaults.Provider}' reachable");
            }
            catch (Exception ex)
            {
                Fail($"Provider '{config.Agents.Defaults.Provider}' unreachable: {ex.Message}");
                failures++;
            }

            // Memory DB connection (postgres/mssql)
            if (MemoryBackend.TryFromValue(config.Memory.Backend, out var mb2))
            {
                if (mb2 == MemoryBackend.Postgres && config.Memory.ConnectionString is not null)
                {
                    try
                    {
                        await using var conn = new Npgsql.NpgsqlConnection(config.Memory.ConnectionString);
                        await conn.OpenAsync(cancellationToken);
                        Ok("Postgres connection OK");
                    }
                    catch (Exception ex)
                    {
                        Fail($"Postgres connection failed: {ex.Message}");
                        failures++;
                    }
                }
                else if (mb2 == MemoryBackend.MsSql && config.Memory.ConnectionString is not null)
                {
                    try
                    {
                        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(config.Memory.ConnectionString);
                        await conn.OpenAsync(cancellationToken);
                        Ok("SQL Server connection OK");
                    }
                    catch (Exception ex)
                    {
                        Fail($"SQL Server connection failed: {ex.Message}");
                        failures++;
                    }
                }
            }

            // Workspace writability
            try
            {
                var testFile = Path.Combine(workspace, ".clawsharp-write-test");
                await File.WriteAllTextAsync(testFile, "", cancellationToken);
                File.Delete(testFile);
                Ok("Workspace is writable");
            }
            catch (Exception ex)
            {
                Warn($"Workspace not writable: {ex.Message}");
                warnings++;
            }

            // SYSTEM.md
            var systemMd = Path.Combine(workspace, "SYSTEM.md");
            if (File.Exists(systemMd))
            {
                Ok("SYSTEM.md found");
            }
            else
            {
                Warn("SYSTEM.md not found (optional)");
                warnings++;
            }

            // Brave Search API key
            if (config.Tools.Brave?.ApiKey is not null)
            {
                Ok("Brave Search API key configured");
            }
            else
            {
                Warn("Brave Search API key not set (web search disabled)");
                warnings++;
            }

            // Runtime
            Ok($".NET {Environment.Version}");
        }

        AnsiConsole.WriteLine();
        if (failures > 0)
        {
            AnsiConsole.MarkupLine($"[red]{failures} failure(s), {warnings} warning(s). Exit code 2.[/]");
            return 2;
        }

        if (warnings > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{warnings} warning(s), no failures. Exit code 1.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]All checks passed. Exit code 0.[/]");
        return 0;
    }

    private static void Ok(string msg)
    {
        AnsiConsole.MarkupLine($"  [green]✓[/] {Markup.Escape(msg)}");
    }

    private static void Warn(string msg)
    {
        AnsiConsole.MarkupLine($"  [yellow]⚠[/] {Markup.Escape(msg)}");
    }

    private static void Fail(string msg)
    {
        AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(msg)}");
    }
}