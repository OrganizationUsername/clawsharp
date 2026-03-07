using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Base64-encoded image source for the Anthropic API.</summary>
internal sealed class ImageSource
{
    /// <summary>Source type (always "base64").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "base64";

    /// <summary>IANA media type (e.g. "image/jpeg", "image/png").</summary>
    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "";

    /// <summary>Base64-encoded image data.</summary>
    [JsonPropertyName("data")]
    public string Data { get; init; } = "";
}