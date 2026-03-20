using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Audio input data for the <c>input_audio</c> content type.</summary>
internal sealed class AudioContentData
{
    /// <summary>Base64-encoded audio data.</summary>
    [JsonPropertyName("data")]
    public string Data { get; init; } = "";

    /// <summary>Audio format (e.g. "wav", "mp3", "ogg", "flac").</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "";
}
