using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>A tool_result content block returning tool output to the Anthropic API.</summary>
internal sealed class ToolResultBlock
{
    /// <summary>Block type (always "tool_result").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "tool_result";

    /// <summary>ID of the tool_use block this result corresponds to.</summary>
    [JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; init; } = "";

    /// <summary>The tool's output text.</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    /// <summary>Whether the tool execution resulted in an error.</summary>
    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; init; }
}