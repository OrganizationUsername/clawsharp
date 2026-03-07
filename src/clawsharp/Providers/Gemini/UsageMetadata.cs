using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Token usage metadata from a Gemini API response.</summary>
internal sealed class UsageMetadata
{
    /// <summary>Number of tokens in the prompt.</summary>
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; init; }

    /// <summary>Number of tokens in the generated candidates.</summary>
    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; init; }

    /// <summary>Number of tokens served from the cached content (context caching).</summary>
    [JsonPropertyName("cachedContentTokenCount")]
    public int CachedContentTokenCount { get; init; }
}