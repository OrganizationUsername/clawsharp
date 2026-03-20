using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// A single SSE chunk from the OpenRouter streaming chat completions API.
/// Extends the standard OpenAI stream chunk with <see cref="OpenRouterUsage"/> (includes cost).
/// </summary>
internal sealed class OpenRouterStreamChunk
{
    /// <summary>List of streaming choices.</summary>
    [JsonPropertyName("choices")]
    public List<OpenRouterStreamChoice>? Choices { get; init; }

    /// <summary>
    /// Token usage data with cost, present on the final chunk
    /// when <c>stream_options.include_usage</c> is set.
    /// </summary>
    [JsonPropertyName("usage")]
    public OpenRouterUsage? Usage { get; init; }

    /// <summary>
    /// Mid-stream error from OpenRouter. Present when a provider error occurs after
    /// streaming has started (HTTP status remains 200 since headers were already sent).
    /// The corresponding choice will have <c>finish_reason: "error"</c>.
    /// </summary>
    [JsonPropertyName("error")]
    public OpenRouterError? Error { get; init; }
}
