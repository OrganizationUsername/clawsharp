using System.Text.Json.Serialization;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// A single choice within a streaming OpenRouter chat completion chunk.
/// Extends the standard OpenAI stream choice with <see cref="NativeFinishReason"/>.
/// </summary>
internal sealed class OpenRouterStreamChoice
{
    /// <summary>The delta content for this streaming choice.</summary>
    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; init; }

    /// <summary>Normalized finish reason (null until the final chunk for this choice).</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }

    /// <summary>
    /// The raw finish reason from the upstream provider before OpenRouter normalizes it.
    /// </summary>
    [JsonPropertyName("native_finish_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeFinishReason { get; init; }
}
