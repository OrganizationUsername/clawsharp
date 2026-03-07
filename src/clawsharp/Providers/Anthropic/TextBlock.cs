using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>A text content block in an Anthropic message.</summary>
internal sealed class TextBlock
{
    /// <summary>Block type (always "text").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    /// <summary>The text content.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}