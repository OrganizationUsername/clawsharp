using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>
/// Represents a parsed Anthropic SSE data payload. A single data JSON is reused for
/// multiple event types: content_block_start, content_block_delta, message_stop, etc.
/// </summary>
internal sealed class StreamEvent
{
    /// <summary>Event type from the data payload (e.g. "content_block_delta", "content_block_start").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Delta sub-object present on content_block_delta events.</summary>
    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; init; }

    /// <summary>Block index -- present on content_block_start and content_block_delta.</summary>
    [JsonPropertyName("index")]
    public int? Index { get; init; }

    /// <summary>Content block -- present on content_block_start events.</summary>
    [JsonPropertyName("content_block")]
    public StreamContentBlock? ContentBlock { get; init; }

    /// <summary>
    /// Top-level usage object present on <c>message_delta</c> events (carries <c>output_tokens</c>).
    /// </summary>
    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }

    /// <summary>
    /// Nested message object present on <c>message_start</c> events.
    /// Contains the full usage block (input_tokens, cache_read_input_tokens, cache_creation_input_tokens).
    /// </summary>
    [JsonPropertyName("message")]
    public StreamMessageStart? Message { get; init; }
}

/// <summary>
/// The <c>message</c> sub-object within a <c>message_start</c> SSE event.
/// Carries the initial usage block with input and cache token counts.
/// </summary>
internal sealed class StreamMessageStart
{
    /// <summary>Usage statistics from the message_start event.</summary>
    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }
}