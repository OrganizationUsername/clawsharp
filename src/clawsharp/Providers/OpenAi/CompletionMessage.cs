using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A single message in an OpenAI chat completion conversation.</summary>
internal sealed class CompletionMessage
{
    /// <summary>The role of the message author ("system", "user", "assistant", "tool").</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    /// <summary>
    /// Message content: either a plain string (text-only) or a list of content parts (multimodal).
    /// Serialises as a JSON string when Parts is null; as a JSON array otherwise.
    /// </summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MessageContent? Content { get; init; }

    /// <summary>Tool call ID for tool-role messages.</summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }

    /// <summary>Optional name of the message author.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>Tool calls requested by the assistant.</summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FunctionToolCall>? ToolCalls { get; init; }

    /// <summary>Reasoning content from models that support chain-of-thought (e.g. o1, DeepSeek-R1).</summary>
    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; init; }
}