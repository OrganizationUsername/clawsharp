namespace Clawsharp.Core;

/// <summary>A response from an LLM provider.</summary>
public sealed record ChatResponse(
    /// <summary>Text content of the response (null if only tool calls).</summary>
    string? Content,
    /// <summary>Tool calls requested by the model.</summary>
    IReadOnlyList<ToolCall>? ToolCalls,
    /// <summary>Reason the model stopped (stop, tool_calls, or length).</summary>
    FinishReason FinishReason,
    /// <summary>Number of input tokens consumed.</summary>
    int? InputTokens = null,
    /// <summary>Number of output tokens generated.</summary>
    int? OutputTokens = null,
    /// <summary>Reasoning/thinking content from models that support it (DeepSeek-R1, Claude Extended Thinking, o1/o3).</summary>
    string? ReasoningContent = null,
    /// <summary>Tokens read from the prompt cache this turn (Anthropic: 0.10× price; OpenAI: 0.50× price).</summary>
    int? CacheReadTokens = null,
    /// <summary>Tokens written to the prompt cache this turn (Anthropic: 1.25× price; OpenAI: no write premium).</summary>
    int? CacheWriteTokens = null
);