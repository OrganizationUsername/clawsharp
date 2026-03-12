using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Telegram;

/// <summary>
/// Request to send a text message to a Telegram chat via the sendMessage Bot API method.
/// </summary>
internal sealed class TelegramSendMessageRequest : IRequest<TelegramSendMessageRequest, TelegramSendMessageResponse>
{
    /// <summary>The numeric chat identifier to send the message to.</summary>
    [JsonPropertyName("chat_id")]
    public long ChatId { get; init; }

    /// <summary>The text content of the message (UTF-8, max 4096 characters).</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>Optional parse mode for message formatting (e.g. "Markdown", "HTML").</summary>
    [JsonPropertyName("parse_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParseMode { get; init; }

    /// <summary>If set, the message will be sent as a reply to the specified message.</summary>
    [JsonPropertyName("reply_to_message_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ReplyToMessageId { get; init; }

    /// <summary>
    /// Unique identifier of the target forum topic (thread) in a supergroup with topics enabled.
    /// If set, the message will be sent to the specified topic instead of the "General" topic.
    /// </summary>
    [JsonPropertyName("message_thread_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MessageThreadId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<TelegramSendMessageRequest> RequestTypeInfo => TelegramJsonContext.Default.TelegramSendMessageRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<TelegramSendMessageResponse> ResponseTypeInfo => TelegramJsonContext.Default.TelegramSendMessageResponse;
}