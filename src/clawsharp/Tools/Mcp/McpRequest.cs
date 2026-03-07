using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Mcp;

/// <summary>JSON-RPC 2.0 request for the MCP protocol.</summary>
public sealed class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }
}

/// <summary>MCP client identification sent during initialize.</summary>
internal sealed record McpClientInfo(string name, string version);

/// <summary>Empty capabilities object sent during initialize.</summary>
internal sealed record McpCapabilities();

/// <summary>Params for the MCP initialize request.</summary>
internal sealed record McpInitializeParams(
    string protocolVersion,
    McpClientInfo clientInfo,
    McpCapabilities capabilities);

/// <summary>Params for the tools/call MCP request.</summary>
internal sealed record McpCallToolParams(string name, JsonElement arguments);