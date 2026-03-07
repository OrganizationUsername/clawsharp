using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Content block header emitted on content_block_start streaming events.</summary>
internal sealed class StreamContentBlock
{
    /// <summary>Block type ("text" or "tool_use").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Tool-use block ID -- set when content_block.type == "tool_use".</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Tool name -- set when content_block.type == "tool_use".</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}