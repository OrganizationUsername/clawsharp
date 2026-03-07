using Clawsharp.Config.Features;
namespace Clawsharp.Core;

/// <summary>A request to an LLM provider for chat completion.</summary>
public sealed record ChatRequest(
    /// <summary>The model identifier to use.</summary>
    string Model,
    /// <summary>Ordered list of conversation messages.</summary>
    IReadOnlyList<ChatMessage> Messages,
    /// <summary>Available tool definitions for function calling.</summary>
    IReadOnlyList<ToolDefinition>? Tools = null,
    /// <summary>Sampling temperature (0.0 - 2.0).</summary>
    float Temperature = 0.7f,
    /// <summary>Maximum number of tokens to generate.</summary>
    int? MaxTokens = null,
    /// <summary>
    /// Stable portion of the system prompt (identity, instructions, tools, memory).
    /// Set by <see cref="SystemPromptBuilder.BuildSplit"/> and used by caching-aware providers
    /// (Anthropic <c>cache_control</c>, OpenAI automatic prefix caching) as the cacheable prefix.
    /// </summary>
    string? SystemStaticPart = null,
    /// <summary>
    /// Dynamic portion of the system prompt (current datetime, channel name).
    /// Changes on every request — never cached.
    /// </summary>
    string? SystemDynamicPart = null,
    /// <summary>
    /// Whether to annotate tool definitions with <c>cache_control</c> for providers that support it
    /// (Anthropic). Defaults to true. Set to false via <see cref="Config.CachingConfig.CacheToolDefinitions"/>
    /// when the tool set changes frequently and tool-definition caching would cause excessive cache misses.
    /// </summary>
    bool CacheToolDefinitions = true,
    /// <summary>
    /// Anthropic: maximum tokens the model may use for internal reasoning (<c>budget_tokens</c>).
    /// 0 means extended thinking is disabled.
    /// </summary>
    int ThinkingBudgetTokens = 0,
    /// <summary>
    /// OpenAI: reasoning effort level (<c>"low"</c>, <c>"medium"</c>, or <c>"high"</c>).
    /// Null means the provider's default reasoning effort is used.
    /// </summary>
    string? ReasoningEffort = null,
    /// <summary>
    /// Gemini: maximum tokens for the model's thinking budget (<c>thinkingBudget</c>).
    /// 0 means thinking configuration is disabled.
    /// </summary>
    int GeminiThinkingBudget = 0
);