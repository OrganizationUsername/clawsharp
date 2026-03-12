using System.Text.Json.Serialization;

namespace Clawsharp.Analytics;

/// <summary>
/// Captures a complete LLM interaction: prompt, thinking, response, cost, and cache data.
/// Persisted to ~/.clawsharp/interactions.jsonl for analytics and later memory analysis.
/// </summary>
public sealed class InteractionRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("channel")]
    public required string Channel { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("user_prompt")]
    public required string UserPrompt { get; init; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    [JsonPropertyName("response")]
    public required string Response { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<ToolCallSummary>? ToolCalls { get; init; }

    [JsonPropertyName("tool_iterations")]
    public int ToolIterations { get; init; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; init; }

    [JsonPropertyName("cache_read_tokens")]
    public long CacheReadTokens { get; init; }

    [JsonPropertyName("cache_write_tokens")]
    public long CacheWriteTokens { get; init; }

    [JsonPropertyName("cost_usd")]
    public double CostUsd { get; init; }

    [JsonPropertyName("cache_savings_usd")]
    public double CacheSavingsUsd { get; init; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Summary of a tool call within an interaction (tool name + truncated result).
/// </summary>
public sealed class ToolCallSummary
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("result_length")]
    public int ResultLength { get; init; }
}
