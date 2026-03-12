using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Telegram;

/// <summary>
/// Request to send a chat action (e.g. "typing") via the Telegram Bot API sendChatAction method.
/// </summary>
internal sealed class TelegramSendChatActionRequest : IRequest<TelegramSendChatActionRequest, TelegramSendChatActionResponse>
{
    /// <summary>The numeric chat identifier to send the action to.</summary>
    [JsonPropertyName("chat_id")]
    public long ChatId { get; init; }

    /// <summary>The action to broadcast (e.g. "typing", "upload_photo").</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "typing";

    /// <summary>
    /// Unique identifier of the target forum topic (thread) in a supergroup with topics enabled.
    /// If set, the action indicator will appear in the specified topic.
    /// </summary>
    [JsonPropertyName("message_thread_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MessageThreadId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<TelegramSendChatActionRequest> RequestTypeInfo => TelegramJsonContext.Default.TelegramSendChatActionRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<TelegramSendChatActionResponse> ResponseTypeInfo => TelegramJsonContext.Default.TelegramSendChatActionResponse;
}