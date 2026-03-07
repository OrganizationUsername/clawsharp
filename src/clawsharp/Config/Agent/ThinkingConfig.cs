namespace Clawsharp.Config.Agent;

/// <summary>
/// Controls extended thinking / reasoning configuration for LLM providers.
/// Each provider uses a different mechanism: Anthropic uses <c>budget_tokens</c>,
/// OpenAI uses <c>reasoning_effort</c>, and Gemini uses <c>thinkingBudget</c>.
/// </summary>
public sealed class ThinkingConfig
{
    /// <summary>
    /// Anthropic: maximum tokens the model may use for internal reasoning (<c>budget_tokens</c>).
    /// Set to 0 (default) to disable extended thinking for Anthropic.
    /// </summary>
    public int BudgetTokens { get; init; }

    /// <summary>
    /// OpenAI: reasoning effort level (<c>"low"</c>, <c>"medium"</c>, or <c>"high"</c>).
    /// Null (default) means the provider's default reasoning effort is used.
    /// </summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// Gemini: maximum tokens for the model's thinking budget (<c>thinkingBudget</c>).
    /// Set to 0 (default) to disable thinking configuration for Gemini.
    /// </summary>
    public int GeminiBudgetTokens { get; init; }
}