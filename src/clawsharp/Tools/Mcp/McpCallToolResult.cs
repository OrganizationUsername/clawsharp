using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Mcp;

/// <summary>Result of a tools/call MCP request.</summary>
public sealed class McpCallToolResult
{
    [JsonPropertyName("content")]
    public List<McpContentBlock> Content { get; init; } = [];

    [JsonPropertyName("isError")]
    public bool IsError { get; init; }
}

/// <summary>A content block in an MCP tool result (type: "text").</summary>
public sealed class McpContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}