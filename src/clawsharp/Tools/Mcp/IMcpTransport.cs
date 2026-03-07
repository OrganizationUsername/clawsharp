using System.Text.Json;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// Abstraction over the JSON-RPC transport layer for MCP servers.
/// Implementations handle sending requests and receiving responses via
/// stdio, SSE, or StreamableHTTP.
/// </summary>
internal interface IMcpTransport : IAsyncDisposable
{
    /// <summary>
    /// Sends a JSON-RPC request and returns the parsed response.
    /// </summary>
    Task<McpResponse> SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct);

    /// <summary>
    /// Sends a JSON-RPC notification (no id, no response expected).
    /// </summary>
    Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct);

    /// <summary>Whether the transport connection is still alive.</summary>
    bool IsConnected { get; }
}