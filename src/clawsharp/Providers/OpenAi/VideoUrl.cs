using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Video URL reference for multimodal content (supports data: URIs for base64 video).</summary>
internal sealed class VideoUrl
{
    /// <summary>The video URL (either an HTTP URL, a YouTube link, or a data: URI with base64 content).</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
}
