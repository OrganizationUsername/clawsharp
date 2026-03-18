using System.Text.Json.Serialization;

namespace Clawsharp.Cost;

/// <summary>A single cost record representing one LLM API call.</summary>
public sealed class CostRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; init; }

    /// <summary>Tokens served from the prompt cache this request.</summary>
    [JsonPropertyName("cache_read_tokens")]
    public long CacheReadTokens { get; init; }

    /// <summary>Tokens written to the prompt cache this request.</summary>
    [JsonPropertyName("cache_write_tokens")]
    public long CacheWriteTokens { get; init; }

    /// <summary>Estimated USD saved versus billing all input at the full uncached rate.</summary>
    [JsonPropertyName("cache_savings_usd")]
    public decimal CacheSavingsUsd { get; init; }

    [JsonPropertyName("cost_usd")]
    public decimal CostUsd { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}