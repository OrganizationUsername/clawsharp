using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>A tool_use content block in an Anthropic assistant message.</summary>
internal sealed class ToolUseBlock
{
    /// <summary>Block type (always "tool_use").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "tool_use";

    /// <summary>Unique identifier for this tool use.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Name of the tool to invoke.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>JSON input arguments for the tool.</summary>
    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }
}