using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Audio output delta in a streaming response.</summary>
internal sealed class StreamAudioDelta
{
    /// <summary>Base64-encoded audio chunk.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>Transcript of the audio being generated.</summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; init; }
}
