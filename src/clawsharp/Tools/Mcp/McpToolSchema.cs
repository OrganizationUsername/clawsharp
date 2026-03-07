using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Mcp;

/// <summary>Represents a tool discovered via MCP tools/list.</summary>
public sealed class McpToolSchema
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; init; }
}