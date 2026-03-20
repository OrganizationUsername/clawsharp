using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>Audio output configuration for models with audio generation capabilities.</summary>
internal sealed class OpenRouterAudioConfig
{
    /// <summary>Voice to use (e.g. "alloy", "echo", "nova", "shimmer").</summary>
    [JsonPropertyName("voice")]
    public string Voice { get; init; } = "alloy";

    /// <summary>Audio format (e.g. "wav", "mp3", "opus", "flac", "pcm16").</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "wav";
}
