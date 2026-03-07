using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Mcp;

/// <summary>Result of a tools/list MCP request.</summary>
public sealed class McpToolsListResult
{
    [JsonPropertyName("tools")]
    public List<McpToolSchema> Tools { get; init; } = [];
}