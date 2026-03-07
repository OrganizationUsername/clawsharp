using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Image URL reference for multimodal content (supports data: URIs for base64 images).</summary>
internal sealed class ImageUrl
{
    /// <summary>The image URL (either an HTTP URL or a data: URI with base64 content).</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    /// <summary>Image detail level: "auto", "low", or "high".</summary>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}