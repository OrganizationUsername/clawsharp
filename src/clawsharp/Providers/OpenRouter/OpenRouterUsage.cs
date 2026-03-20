using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// Token usage statistics from an OpenRouter API response.
/// Extends standard OpenAI usage with <see cref="Cost"/> (actual USD charged)
/// and <see cref="IsByok"/> (bring-your-own-key indicator).
/// </summary>
internal sealed class OpenRouterUsage
{
    /// <summary>Number of tokens in the prompt.</summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    /// <summary>Number of tokens in the completion.</summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    /// <summary>Total tokens used (prompt + completion).</summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }

    /// <summary>Breakdown of prompt tokens, including cache hit and write counts.</summary>
    [JsonPropertyName("prompt_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterPromptTokensDetails? PromptDetails { get; init; }

    /// <summary>Breakdown of completion tokens, including reasoning tokens.</summary>
    [JsonPropertyName("completion_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenRouterCompletionTokensDetails? CompletionDetails { get; init; }

    /// <summary>Actual cost in USD credits charged by OpenRouter for this request.</summary>
    [JsonPropertyName("cost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Cost { get; init; }

    /// <summary>Whether this request was served using the caller's own API key (bring-your-own-key).</summary>
    [JsonPropertyName("is_byok")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsByok { get; init; }
}

/// <summary>Breakdown of prompt token counts including cache metrics.</summary>
internal sealed class OpenRouterPromptTokensDetails
{
    /// <summary>Tokens served from the prompt cache (billed at reduced rate).</summary>
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; init; }

    /// <summary>Tokens written to the prompt cache this turn.</summary>
    [JsonPropertyName("cache_write_tokens")]
    public int CacheWriteTokens { get; init; }
}

/// <summary>Breakdown of completion token counts.</summary>
internal sealed class OpenRouterCompletionTokensDetails
{
    /// <summary>Tokens used for internal reasoning (e.g. o1/o3 chain-of-thought).</summary>
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; init; }
}
