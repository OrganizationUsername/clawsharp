using System.Text.Json.Serialization;

namespace Clawsharp.Core.Transcription;

/// <summary>Response DTO for OpenAI/Groq Whisper transcription API.</summary>
internal sealed class VoiceTranscriptResult
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}