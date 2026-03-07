using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// BackgroundService that starts all configured MCP servers on startup,
/// registers their tools in the ToolRegistry, and disposes them on shutdown.
/// Monitors connections and restarts them with exponential backoff on failure.
/// Supports stdio, SSE, and StreamableHTTP transports.
/// </summary>
public sealed partial class McpHostedService : LifecycleBackgroundService
{
    private readonly AppConfig _config;

    private readonly IToolRegistry _toolRegistry;

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ILogger<McpHostedService> _logger;

    private readonly List<ManagedMcpServer> _servers = [];

    /// <summary>Maximum backoff delay between restart attempts.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    /// <summary>Base delay for exponential backoff (doubles each attempt).</summary>
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);

    /// <summary>How often the watchdog polls for crashed processes.</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);

    public McpHostedService(
        IOptions<AppConfig> options,
        IToolRegistry toolRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<McpHostedService> logger)
    {
        _config = options.Value;
        _toolRegistry = toolRegistry;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.McpServers is not { Count: > 0 })
        {
            LogNoServersConfigured(_logger);
            return;
        }

        LogStartingServers(_logger, _config.McpServers.Count);

        foreach (var (serverName, serverConfig) in _config.McpServers)
        {
            var managed = new ManagedMcpServer(serverName, serverConfig);
            _servers.Add(managed);

            try
            {
                await StartServerAsync(managed, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogServerStartFailed(_logger, serverName, ex);
            }
        }

        LogInitializationComplete(_logger,
            _servers.Count(s => s.Client is not null),
            _servers.Where(s => s.Client is not null).Sum(s => s.Client!.Tools.Count));

        // Watchdog loop -- monitors for crashed processes and restarts them
        await RunWatchdogAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>Start (or restart) a single MCP server and register its tools.</summary>
    private async Task StartServerAsync(ManagedMcpServer managed, CancellationToken ct)
    {
        var transportType = managed.Config.ResolveTransportType();
        LogServerStarting(_logger, managed.Name, transportType,
            transportType == "stdio"
                ? $"{managed.Config.Command} {(managed.Config.Args is not null ? string.Join(" ", managed.Config.Args) : "")}"
                : managed.Config.Url ?? "");

        var transport = await CreateTransportAsync(managed.Name, managed.Config, transportType, ct)
            .ConfigureAwait(false);

        McpClient client;
        try
        {
            client = await McpClient.StartAsync(managed.Name, transport, _logger, ct).ConfigureAwait(false);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        // Dispose previous client if restarting
        if (managed.Client is not null)
        {
            try
            {
                await managed.Client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogServerDisposeFailed(_logger, managed.Name, ex);
            }
        }

        managed.Client = client;
        managed.RestartCount = 0; // Reset on successful start

        // Register each discovered tool in the tool registry
        foreach (var tool in client.Tools)
        {
            var adapter = new McpToolAdapter(client, tool);
            _toolRegistry.Register(adapter);
            LogToolRegistered(_logger, tool.Name, managed.Name);
        }
    }

    /// <summary>
    /// Creates the appropriate transport for the given config.
    /// </summary>
    private async Task<IMcpTransport> CreateTransportAsync(
        string serverName, McpServerConfig config, string transportType, CancellationToken ct)
    {
        switch (transportType.ToLowerInvariant())
        {
            case "sse":
            {
                if (string.IsNullOrEmpty(config.Url))
                {
                    throw new InvalidOperationException(
                        $"MCP server '{serverName}' has transport type 'sse' but no URL configured.");
                }

                var httpClient = _httpClientFactory.CreateClient("mcp");
                var sseUri = new Uri(config.Url);
                var transport = new SseMcpTransport(httpClient, sseUri, serverName, config.Headers, _logger);
                await transport.ConnectAsync(ct).ConfigureAwait(false);
                return transport;
            }

            case "streamablehttp":
            {
                if (string.IsNullOrEmpty(config.Url))
                {
                    throw new InvalidOperationException(
                        $"MCP server '{serverName}' has transport type 'streamableHttp' but no URL configured.");
                }

                var httpClient = _httpClientFactory.CreateClient("mcp");
                var endpointUri = new Uri(config.Url);
                return new StreamableHttpMcpTransport(httpClient, endpointUri, serverName, config.Headers, _logger);
            }

            case "stdio":
            default:
            {
                if (string.IsNullOrEmpty(config.Command))
                {
                    throw new InvalidOperationException(
                        $"MCP server '{serverName}' has transport type 'stdio' but no command configured.");
                }

                return StdioMcpTransport.Start(serverName, config, _logger);
            }
        }
    }

    /// <summary>
    /// Periodically checks if any MCP server process has exited unexpectedly
    /// and restarts it with exponential backoff.
    /// </summary>
    private async Task RunWatchdogAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WatchdogInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            foreach (var managed in _servers)
            {
                if (managed.Client is null)
                {
                    // Never started successfully -- try again with backoff
                    await TryRestartAsync(managed, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!managed.Client.HasExited)
                {
                    continue;
                }

                LogServerCrashed(_logger, managed.Name, managed.Client.ExitCode);
                await TryRestartAsync(managed, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Attempt to restart a server with exponential backoff.</summary>
    private async Task TryRestartAsync(ManagedMcpServer managed, CancellationToken ct)
    {
        managed.RestartCount++;
        var delay = CalculateBackoff(managed.RestartCount);

        LogServerRestarting(_logger, managed.Name, managed.RestartCount, (int)delay.TotalSeconds);

        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await StartServerAsync(managed, ct).ConfigureAwait(false);
            LogServerRestarted(_logger, managed.Name, managed.RestartCount);
        }
        catch (Exception ex)
        {
            LogServerRestartFailed(_logger, managed.Name, managed.RestartCount, ex);
        }
    }

    /// <summary>Exponential backoff: 1s, 2s, 4s, 8s, ..., capped at 60s.</summary>
    private static TimeSpan CalculateBackoff(int restartCount)
    {
        var seconds = BaseBackoff.TotalSeconds * Math.Pow(2, restartCount - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogStoppingServers(_logger, _servers.Count);

        foreach (var managed in _servers)
        {
            if (managed.Client is null)
            {
                continue;
            }

            try
            {
                await managed.Client.DisposeAsync().ConfigureAwait(false);
                LogServerStopped(_logger, managed.Name);
            }
            catch (Exception ex)
            {
                LogServerStopFailed(_logger, managed.Name, ex);
            }
        }

        _servers.Clear();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tracks the state of a single managed MCP server for the watchdog.</summary>
    private sealed class ManagedMcpServer(string name, McpServerConfig config)
    {
        public string Name { get; } = name;

        public McpServerConfig Config { get; } = config;

        public McpClient? Client { get; set; }

        /// <summary>Number of consecutive restart attempts since the last successful start.</summary>
        public int RestartCount { get; set; }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Starting MCP server '{ServerName}' [{TransportType}]: {Detail}")]
    private static partial void LogServerStarting(ILogger logger, string serverName, string transportType, string detail);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Registered MCP tool '{ToolName}' from server '{ServerName}'")]
    private static partial void LogToolRegistered(ILogger logger, string toolName, string serverName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Failed to start MCP server '{ServerName}' -- skipping")]
    private static partial void LogServerStartFailed(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "MCP server '{ServerName}' crashed (exit code {ExitCode}), scheduling restart")]
    private static partial void LogServerCrashed(ILogger logger, string serverName, int? exitCode);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information,
        Message = "Restarting MCP server '{ServerName}' (attempt {Attempt}, backoff {BackoffSeconds}s)")]
    private static partial void LogServerRestarting(ILogger logger, string serverName, int attempt, int backoffSeconds);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information,
        Message = "MCP server '{ServerName}' restarted successfully after {Attempt} attempt(s)")]
    private static partial void LogServerRestarted(ILogger logger, string serverName, int attempt);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error,
        Message = "MCP server '{ServerName}' restart attempt {Attempt} failed")]
    private static partial void LogServerRestartFailed(ILogger logger, string serverName, int attempt, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Error disposing previous MCP server '{ServerName}' client")]
    private static partial void LogServerDisposeFailed(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "No MCP servers configured")]
    private static partial void LogNoServersConfigured(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Starting {Count} MCP server(s)")]
    private static partial void LogStartingServers(ILogger logger, int count);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "MCP initialization complete: {ClientCount} server(s) running, {ToolCount} tool(s) registered")]
    private static partial void LogInitializationComplete(ILogger logger, int clientCount, int toolCount);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Stopping {Count} MCP server(s)")]
    private static partial void LogStoppingServers(ILogger logger, int count);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "MCP server '{ServerName}' stopped")]
    private static partial void LogServerStopped(ILogger logger, string serverName);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "Error stopping MCP server '{ServerName}'")]
    private static partial void LogServerStopFailed(ILogger logger, string serverName, Exception exception);
}