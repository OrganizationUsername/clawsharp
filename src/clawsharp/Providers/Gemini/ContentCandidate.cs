using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>A single candidate in a Gemini generateContent response.</summary>
internal sealed class ContentCandidate
{
    /// <summary>The generated content.</summary>
    [JsonPropertyName("content")]
    public ContentItem Content { get; init; } = new();

    /// <summary>Reason the model stopped generating ("STOP", "MAX_TOKENS", etc.).</summary>
    [JsonPropertyName("finishReason")]
    public string FinishReason { get; init; } = "STOP";
}