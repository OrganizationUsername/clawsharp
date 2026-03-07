using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>
/// Extended thinking configuration for the Anthropic Messages API.
/// When included, the model uses internal reasoning up to <see cref="BudgetTokens"/> tokens
/// before producing its visible response.
/// </summary>
internal sealed class AnthropicThinking
{
    /// <summary>Thinking type — always "enabled" when present.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "enabled";

    /// <summary>Maximum tokens the model may use for internal reasoning.</summary>
    [JsonPropertyName("budget_tokens")]
    public int BudgetTokens { get; init; }
}