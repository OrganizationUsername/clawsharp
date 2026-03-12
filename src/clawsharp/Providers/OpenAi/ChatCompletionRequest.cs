using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Providers.OpenAi;

/// <summary>
/// Request body for the OpenAI Chat Completions API (POST /v1/chat/completions).
/// </summary>
internal sealed class ChatCompletionRequest : IRequest<ChatCompletionRequest, ChatCompletionResponse>
{
    /// <summary>Model identifier (e.g. "gpt-4o-mini").</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    /// <summary>Ordered list of conversation messages.</summary>
    [JsonPropertyName("messages")]
    public List<CompletionMessage> Messages { get; init; } = [];

    /// <summary>Sampling temperature (0.0 - 2.0).</summary>
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Maximum number of tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }

    /// <summary>Available tool definitions for function calling.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FunctionTool>? Tools { get; init; }

    /// <summary>Tool choice strategy ("auto", "none", or a specific tool).</summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; init; }

    /// <summary>
    /// Reasoning effort for models that support it (e.g. o1, o3).
    /// Valid values: "low", "medium", "high". Null means provider default.
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; init; }

    /// <summary>Whether to stream the response via SSE.</summary>
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; init; }

    /// <summary>Options controlling stream behavior (e.g. include usage in final chunk).</summary>
    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamOptions? StreamOptions { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<ChatCompletionRequest> RequestTypeInfo => OpenAiJsonContext.Default.ChatCompletionRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<ChatCompletionResponse> ResponseTypeInfo => OpenAiJsonContext.Default.ChatCompletionResponse;
}

/// <summary>Options for streaming responses.</summary>
internal sealed class StreamOptions
{
    /// <summary>When true, the final streaming chunk includes a <c>usage</c> field with token counts.</summary>
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; init; }
}