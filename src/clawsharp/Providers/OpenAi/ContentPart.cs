using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A single content part within a multimodal message (text or image).</summary>
internal sealed class ContentPart
{
    /// <summary>Part type: "text" or "image_url".</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>Text content (when type is "text").</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>Image URL data (when type is "image_url").</summary>
    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageUrl? ImageUrl { get; init; }
}