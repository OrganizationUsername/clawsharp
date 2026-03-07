using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>
/// Thinking configuration for the Gemini API.
/// Controls the token budget allocated for the model's internal reasoning.
/// </summary>
internal sealed class GeminiThinkingConfig
{
    /// <summary>Maximum tokens the model may use for internal thinking.</summary>
    [JsonPropertyName("thinkingBudget")]
    public int ThinkingBudget { get; init; }
}