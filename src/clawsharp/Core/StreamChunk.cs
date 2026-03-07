namespace Clawsharp.Core;

/// <summary>Discriminated union of chunks emitted by a streaming LLM call.</summary>
public abstract record StreamChunk;

/// <summary>A text token delta from the assistant's response.</summary>
public sealed record TextDeltaChunk(string Delta) : StreamChunk;

/// <summary>
///     A fragment of a tool call being streamed incrementally.
///     Index identifies which tool call (when multiple tool calls are emitted in one turn).
///     Id and Name are populated on the first chunk for that index; ArgumentsDelta accumulates the JSON.
/// </summary>
public sealed record ToolCallChunk(int Index, string? Id, string? Name, string? ArgumentsDelta) : StreamChunk;

/// <summary>A reasoning/thinking delta from models that support extended thinking (Anthropic, o1, DeepSeek-R1).</summary>
public sealed record ThinkingDeltaChunk(string Delta) : StreamChunk;

/// <summary>
/// Carries token usage data extracted from streaming events (e.g. Anthropic <c>message_start</c>/<c>message_delta</c>,
/// OpenAI final chunk with <c>usage</c>). Multiple usage chunks may be emitted per stream; the consumer should
/// accumulate them.
/// </summary>
public sealed record StreamUsageChunk(
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens = 0,
    int CacheWriteTokens = 0
) : StreamChunk;

/// <summary>Sentinel that signals the stream is complete.</summary>
public sealed record StreamDoneChunk : StreamChunk;