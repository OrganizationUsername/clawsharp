using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Token usage statistics from an OpenAI API response.</summary>
internal sealed class TokenUsage
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

    /// <summary>Breakdown of prompt tokens, including cache hit counts.</summary>
    [JsonPropertyName("prompt_tokens_details")]
    public PromptTokensDetails? Details { get; init; }
}