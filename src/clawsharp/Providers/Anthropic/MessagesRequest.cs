using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Request body for the Anthropic Messages API (POST /v1/messages).</summary>
internal sealed class MessagesRequest : IRequest<MessagesRequest, MessagesResponse>
{
    /// <summary>Model identifier (e.g. "claude-sonnet-4-20250514").</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    /// <summary>Maximum tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Sampling temperature. Nullable because Anthropic requires this to be absent
    /// when extended thinking is enabled.
    /// </summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; init; } = 0.7f;

    /// <summary>
    /// System prompt as a content-block array.
    /// Using an array (rather than a plain string) is required for <c>cache_control</c> annotations.
    /// The Anthropic API accepts both forms.
    /// </summary>
    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AnthropicSystemBlock>? System { get; init; }

    /// <summary>Conversation messages.</summary>
    [JsonPropertyName("messages")]
    public List<ConversationMessage> Messages { get; init; } = [];

    /// <summary>Available tools.</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; init; }

    /// <summary>
    /// Extended thinking configuration. When set, the model uses internal reasoning
    /// before producing its visible response. Requires <see cref="Temperature"/> to be unset (null).
    /// </summary>
    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicThinking? Thinking { get; init; }

    /// <summary>Whether to stream the response via SSE.</summary>
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = ClawsharpConstants.AnthropicDefaultBaseUrl + "/v1/messages";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MessagesRequest> RequestTypeInfo => AnthropicJsonContext.Default.MessagesRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MessagesResponse> ResponseTypeInfo => AnthropicJsonContext.Default.MessagesResponse;
}