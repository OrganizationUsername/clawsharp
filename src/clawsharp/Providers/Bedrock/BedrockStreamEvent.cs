using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Bedrock;

// ── Stream Event Models ─────────────────────────────────────────────────────
// JSON payloads inside each binary event-stream message from ConverseStream.

/// <summary>Initial event indicating the assistant role.</summary>
internal sealed class BedrockStreamMessageStart
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

/// <summary>Signals the start of a new content block (text or tool use).</summary>
internal sealed class BedrockStreamContentBlockStart
{
    [JsonPropertyName("contentBlockIndex")]
    public int ContentBlockIndex { get; init; }

    [JsonPropertyName("start")]
    public BedrockStreamBlockStart? Start { get; init; }
}

/// <summary>Start payload — present when the block is a tool use invocation.</summary>
internal sealed class BedrockStreamBlockStart
{
    [JsonPropertyName("toolUse")]
    public BedrockStreamToolUseStart? ToolUse { get; init; }
}

/// <summary>Tool use identity emitted at block start.</summary>
internal sealed class BedrockStreamToolUseStart
{
    [JsonPropertyName("toolUseId")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>Incremental delta for a content block (text token or tool argument fragment).</summary>
internal sealed class BedrockStreamContentBlockDelta
{
    [JsonPropertyName("contentBlockIndex")]
    public int ContentBlockIndex { get; init; }

    [JsonPropertyName("delta")]
    public BedrockStreamDelta? Delta { get; init; }
}

/// <summary>Delta payload — exactly one of Text or ToolUse is populated.</summary>
internal sealed class BedrockStreamDelta
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("toolUse")]
    public BedrockStreamToolUseDelta? ToolUse { get; init; }
}

/// <summary>Incremental tool use argument JSON fragment.</summary>
internal sealed class BedrockStreamToolUseDelta
{
    [JsonPropertyName("input")]
    public string? Input { get; init; }
}

/// <summary>Final metadata event containing token usage.</summary>
internal sealed class BedrockStreamMetadata
{
    [JsonPropertyName("usage")]
    public BedrockStreamUsage? Usage { get; init; }
}

/// <summary>Token usage counters from the stream metadata event.</summary>
internal sealed class BedrockStreamUsage
{
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; init; }
}