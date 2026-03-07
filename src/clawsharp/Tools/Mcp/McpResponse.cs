using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Mcp;

/// <summary>JSON-RPC 2.0 response from an MCP server.</summary>
public sealed class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public McpError? Error { get; init; }
}

/// <summary>JSON-RPC error object.</summary>
public sealed class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}