using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Telegram;

/// <summary>
/// Request to edit the text of an existing Telegram message via the editMessageText Bot API method.
/// </summary>
internal sealed class TelegramEditMessageTextRequest : IRequest<TelegramEditMessageTextRequest, TelegramSendMessageResponse>
{
    /// <summary>The numeric chat identifier containing the message to edit.</summary>
    [JsonPropertyName("chat_id")]
    public long ChatId { get; init; }

    /// <summary>The identifier of the message to edit.</summary>
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }

    /// <summary>The new text content of the message (UTF-8, max 4096 characters).</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>
    /// Unique identifier of the target forum topic (thread) in a supergroup with topics enabled.
    /// Required when editing messages in forum topics to ensure the edit targets the correct topic.
    /// </summary>
    [JsonPropertyName("message_thread_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MessageThreadId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<TelegramEditMessageTextRequest> RequestTypeInfo => TelegramJsonContext.Default.TelegramEditMessageTextRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<TelegramSendMessageResponse> ResponseTypeInfo => TelegramJsonContext.Default.TelegramSendMessageResponse;
}