using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Response from the Gemini generateContent API.</summary>
internal sealed class GenerateContentResponse
{
    /// <summary>Generated content candidates.</summary>
    [JsonPropertyName("candidates")]
    public List<ContentCandidate> Candidates { get; init; } = [];

    /// <summary>Token usage metadata.</summary>
    [JsonPropertyName("usageMetadata")]
    public UsageMetadata? UsageMetadata { get; init; }

    /// <summary>Error information when the API returns an error response.</summary>
    [JsonPropertyName("error")]
    public GeminiApiError? Error { get; init; }
}