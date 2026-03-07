using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Bedrock;

// ── Request Models ──────────────────────────────────────────────────────────

/// <summary>Top-level Bedrock Converse API request.</summary>
internal sealed class BedrockConverseRequest
{
    [JsonPropertyName("messages")]
    public List<BedrockMessage> Messages { get; set; } = [];

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BedrockSystemContent>? System { get; set; }

    [JsonPropertyName("inferenceConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BedrockInferenceConfig? InferenceConfig { get; set; }

    [JsonPropertyName("toolConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BedrockToolConfig? ToolConfig { get; set; }
}

/// <summary>A message in the Bedrock Converse conversation.</summary>
internal sealed class BedrockMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public List<BedrockContentBlock> Content { get; set; } = [];
}

/// <summary>System message content block.</summary>
internal sealed class BedrockSystemContent
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>A content block within a message — text, image, toolUse, or toolResult.</summary>
internal sealed class BedrockContentBlock
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BedrockImageBlock? Image { get; set; }

    [JsonPropertyName("toolUse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BedrockToolUse? ToolUse { get; set; }

    [JsonPropertyName("toolResult")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BedrockToolResult? ToolResult { get; set; }
}

/// <summary>Image content block for the Bedrock Converse API.</summary>
internal sealed class BedrockImageBlock
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("source")]
    public BedrockImageSource Source { get; set; } = new();
}

/// <summary>Image source — inline base64 bytes.</summary>
internal sealed class BedrockImageSource
{
    [JsonPropertyName("bytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Bytes { get; set; }
}

/// <summary>Tool invocation requested by the model.</summary>
internal sealed class BedrockToolUse
{
    [JsonPropertyName("toolUseId")]
    public string ToolUseId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("input")]
    public JsonElement Input { get; set; }
}

/// <summary>Result of a tool invocation sent back to the model.</summary>
internal sealed class BedrockToolResult
{
    [JsonPropertyName("toolUseId")]
    public string ToolUseId { get; set; } = "";

    [JsonPropertyName("content")]
    public List<BedrockToolResultContent> Content { get; set; } = [];
}

/// <summary>Content within a tool result.</summary>
internal sealed class BedrockToolResultContent
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>Inference configuration (temperature, max tokens).</summary>
internal sealed class BedrockInferenceConfig
{
    [JsonPropertyName("maxTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }
}

/// <summary>Tool configuration wrapping tool specs.</summary>
internal sealed class BedrockToolConfig
{
    [JsonPropertyName("tools")]
    public List<BedrockToolSpec> Tools { get; set; } = [];
}

/// <summary>A single tool specification.</summary>
internal sealed class BedrockToolSpec
{
    [JsonPropertyName("toolSpec")]
    public BedrockToolSpecInner Spec { get; set; } = new();
}

/// <summary>Inner tool specification with name, description, and input schema.</summary>
internal sealed class BedrockToolSpecInner
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputSchema")]
    public BedrockInputSchema InputSchema { get; set; } = new();
}

/// <summary>Input schema wrapper — the "json" key holds the raw JSON Schema.</summary>
internal sealed class BedrockInputSchema
{
    [JsonPropertyName("json")]
    public JsonElement Json { get; set; }
}

// ── Response Models ─────────────────────────────────────────────────────────

/// <summary>Top-level Bedrock Converse API response.</summary>
internal sealed class BedrockConverseResponse
{
    [JsonPropertyName("output")]
    public BedrockOutput? Output { get; set; }

    [JsonPropertyName("usage")]
    public BedrockUsage? Usage { get; set; }

    [JsonPropertyName("stopReason")]
    public string? StopReason { get; set; }
}

/// <summary>Output wrapper containing the assistant message.</summary>
internal sealed class BedrockOutput
{
    [JsonPropertyName("message")]
    public BedrockMessage? Message { get; set; }
}

/// <summary>Token usage information.</summary>
internal sealed class BedrockUsage
{
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("cacheReadInputTokenCount")]
    public int CacheReadInputTokenCount { get; set; }

    [JsonPropertyName("cacheWriteInputTokenCount")]
    public int CacheWriteInputTokenCount { get; set; }
}

// ── Source-Generated JSON Context ───────────────────────────────────────────

[JsonSerializable(typeof(BedrockConverseRequest))]
[JsonSerializable(typeof(BedrockConverseResponse))]
[JsonSerializable(typeof(BedrockMessage))]
[JsonSerializable(typeof(BedrockSystemContent))]
[JsonSerializable(typeof(BedrockContentBlock))]
[JsonSerializable(typeof(BedrockImageBlock))]
[JsonSerializable(typeof(BedrockImageSource))]
[JsonSerializable(typeof(BedrockToolUse))]
[JsonSerializable(typeof(BedrockToolResult))]
[JsonSerializable(typeof(BedrockToolResultContent))]
[JsonSerializable(typeof(BedrockInferenceConfig))]
[JsonSerializable(typeof(BedrockToolConfig))]
[JsonSerializable(typeof(BedrockToolSpec))]
[JsonSerializable(typeof(BedrockToolSpecInner))]
[JsonSerializable(typeof(BedrockInputSchema))]
[JsonSerializable(typeof(BedrockOutput))]
[JsonSerializable(typeof(BedrockUsage))]
[JsonSerializable(typeof(List<BedrockMessage>))]
[JsonSerializable(typeof(List<BedrockContentBlock>))]
[JsonSerializable(typeof(List<BedrockSystemContent>))]
[JsonSerializable(typeof(List<BedrockToolSpec>))]
[JsonSerializable(typeof(List<BedrockToolResultContent>))]
// Stream event types (ConverseStream binary event-stream payloads)
[JsonSerializable(typeof(BedrockStreamMessageStart))]
[JsonSerializable(typeof(BedrockStreamContentBlockStart))]
[JsonSerializable(typeof(BedrockStreamContentBlockDelta))]
[JsonSerializable(typeof(BedrockStreamMetadata))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BedrockJsonContext : JsonSerializerContext;