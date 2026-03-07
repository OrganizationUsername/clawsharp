using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Token usage statistics from an Anthropic API response.</summary>
internal sealed class TokenUsage
{
    /// <summary>Number of input tokens consumed.</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    /// <summary>Number of output tokens generated.</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    /// <summary>Tokens written to the prompt cache this request (billed at 1.25× input price).</summary>
    [JsonPropertyName("cache_creation_input_tokens")]
    public int CacheCreationInputTokens { get; init; }

    /// <summary>Tokens read from the prompt cache this request (billed at 0.10× input price).</summary>
    [JsonPropertyName("cache_read_input_tokens")]
    public int CacheReadInputTokens { get; init; }
}