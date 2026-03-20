using System.Text.Json.Serialization;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// A single choice within an OpenRouter chat completion response.
/// Extends the standard OpenAI choice with <see cref="NativeFinishReason"/>.
/// </summary>
internal sealed class OpenRouterChoice
{
    /// <summary>The assistant's message for this choice.</summary>
    [JsonPropertyName("message")]
    public CompletionMessage Message { get; init; } = new();

    /// <summary>Normalized finish reason ("stop", "tool_calls", "length", "content_filter").</summary>
    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; init; } = "stop";

    /// <summary>
    /// The raw finish reason from the upstream provider before OpenRouter normalizes it.
    /// Useful for debugging provider-specific behavior.
    /// </summary>
    [JsonPropertyName("native_finish_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeFinishReason { get; init; }

    /// <summary>
    /// Per-choice error from the upstream provider. Present when the provider returned
    /// an error for this specific choice (e.g. content moderation, provider disconnect).
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterError? Error { get; init; }
}
