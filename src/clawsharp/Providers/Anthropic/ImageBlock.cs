using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>An image content block for multimodal Anthropic messages.</summary>
internal sealed class ImageBlock
{
    /// <summary>Block type (always "image").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "image";

    /// <summary>The image source data.</summary>
    [JsonPropertyName("source")]
    public ImageSource Source { get; init; } = new();
}