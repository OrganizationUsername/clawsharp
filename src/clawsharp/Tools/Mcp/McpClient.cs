using System.Text.Json;
using Microsoft.Extensions.Logging;
using Clawsharp.Config.Features;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// High-level MCP client that wraps an <see cref="IMcpTransport"/> and provides
/// the initialize/discover/call-tool workflow. Transport-agnostic: works with
/// stdio, SSE, or StreamableHTTP transports.
/// </summary>
public sealed partial class McpClient : IAsyncDisposable
{
    private readonly IMcpTransport _transport;

    private readonly ILogger _logger;

    public string ServerName { get; }

    public IReadOnlyList<McpToolSchema> Tools { get; private set; } = [];

    /// <summary>Whether the underlying transport connection is still alive.</summary>
    public bool HasExited => !_transport.IsConnected;

    /// <summary>
    /// Exit code of the MCP server process (stdio only, null for HTTP transports or if still running).
    /// </summary>
    public int? ExitCode =>
        _transport is StdioMcpTransport stdio ? stdio.ExitCode : null;

    private McpClient(string serverName, IMcpTransport transport, ILogger logger)
    {
        ServerName = serverName;
        _transport = transport;
        _logger = logger;
    }

    /// <summary>
    /// Creates an MCP client with the given transport, performs the initialize handshake,
    /// and discovers available tools via tools/list.
    /// </summary>
    internal static async Task<McpClient> StartAsync(
        string serverName,
        IMcpTransport transport,
        ILogger logger,
        CancellationToken ct)
    {
        var client = new McpClient(serverName, transport, logger);

        try
        {
            await client.InitializeAsync(ct).ConfigureAwait(false);
            await client.DiscoverToolsAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return client;
    }

    /// <summary>
    /// Legacy overload for stdio transport — starts a child process from config,
    /// performs the initialize handshake, and discovers tools.
    /// </summary>
    public static async Task<McpClient> StartAsync(
        string serverName,
        McpServerConfig config,
        ILogger logger,
        CancellationToken ct)
    {
        var transport = StdioMcpTransport.Start(serverName, config, logger);

        try
        {
            return await StartAsync(serverName, transport, logger, ct).ConfigureAwait(false);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        // Send initialize request
        var initParams = JsonSerializer.SerializeToElement(
            new McpInitializeParams(
                "2024-11-05",
                new McpClientInfo("clawsharp", "1.0"),
                new McpCapabilities()),
            McpJsonContext.Default.McpInitializeParams);

        var response = await _transport.SendRequestAsync("initialize", initParams, ct).ConfigureAwait(false);
        if (response.Error is not null)
        {
            throw new InvalidOperationException(
                $"MCP server '{ServerName}' initialize error: [{response.Error.Code}] {response.Error.Message}");
        }

        LogServerInitialized(_logger, ServerName);

        // Send initialized notification (no id, no response expected)
        await _transport.SendNotificationAsync("notifications/initialized", null, ct).ConfigureAwait(false);
    }

    private async Task DiscoverToolsAsync(CancellationToken ct)
    {
        var response = await _transport.SendRequestAsync("tools/list", null, ct).ConfigureAwait(false);
        if (response.Error is not null)
        {
            LogToolsListError(_logger, ServerName, response.Error.Code, response.Error.Message);
            Tools = [];
            return;
        }

        if (response.Result is { } result)
        {
            var toolsList = JsonSerializer.Deserialize(
                result.GetRawText(),
                McpJsonContext.Default.McpToolsListResult);

            Tools = toolsList?.Tools ?? [];
        }

        LogToolsDiscovered(_logger, ServerName, Tools.Count, string.Join(", ", Tools.Select(t => t.Name)));
    }

    /// <summary>
    /// Calls a tool on the MCP server and returns the text result.
    /// </summary>
    public async Task<string> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct)
    {
        JsonElement arguments;
        try
        {
            using var argsDoc = JsonDocument.Parse(
                string.IsNullOrEmpty(argumentsJson) ? "{}" : argumentsJson);
            arguments = argsDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var fallbackDoc = JsonDocument.Parse("{}");
            arguments = fallbackDoc.RootElement.Clone();
        }

        var callParams = JsonSerializer.SerializeToElement(
            new McpCallToolParams(toolName, arguments),
            McpJsonContext.Default.McpCallToolParams);

        McpResponse response;
        try
        {
            response = await _transport.SendRequestAsync("tools/call", callParams, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // External cancellation — propagate so McpToolAdapter can re-throw.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Transport/server-side cancellation — let McpToolAdapter return a user-visible error.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP tool call failed");
            return "Error: MCP tool call failed.";
        }

        if (response.Error is not null)
        {
            return $"MCP error [{response.Error.Code}]: {response.Error.Message}";
        }

        if (response.Result is not { } resultElement)
        {
            return "(empty result)";
        }

        // Parse the result as McpCallToolResult
        try
        {
            var callResult = JsonSerializer.Deserialize(
                resultElement.GetRawText(),
                McpJsonContext.Default.McpCallToolResult);

            if (callResult is null)
            {
                return "(null result)";
            }

            var textParts = callResult.Content
                                      .Where(c => string.Equals(c.Type, McpContentType.Text, StringComparison.Ordinal))
                                      .Select(c => c.Text ?? "")
                                      .ToList();

            return textParts.Count > 0 ? string.Join("\n", textParts) : "(no text content)";
        }
        catch (JsonException ex)
        {
            LogUnparseableResult(_logger, ServerName, ex);
            return resultElement.GetRawText();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "MCP server '{ServerName}' initialized (protocol 2024-11-05)")]
    private static partial void LogServerInitialized(ILogger logger, string serverName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "MCP server '{ServerName}' discovered {Count} tool(s): {Names}")]
    private static partial void LogToolsDiscovered(ILogger logger, string serverName, int count, string names);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "MCP server '{ServerName}' returned unparseable tools/call result")]
    private static partial void LogUnparseableResult(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "MCP server '{ServerName}' tools/list error: [{Code}] {Message}")]
    private static partial void LogToolsListError(ILogger logger, string serverName, int code, string message);
}