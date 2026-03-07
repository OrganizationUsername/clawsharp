using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a message received from the Telegram Bot API.</summary>
internal sealed class TelegramMessage
{
    /// <summary>Unique message identifier within this chat.</summary>
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }

    /// <summary>
    /// For messages in forum topics (supergroups with topics enabled), the unique identifier
    /// of the forum topic the message belongs to. Used to scope replies to the correct topic.
    /// </summary>
    [JsonPropertyName("message_thread_id")]
    public long? MessageThreadId { get; init; }

    /// <summary>The sender of the message.</summary>
    [JsonPropertyName("from")]
    public TelegramUser? From { get; init; }

    /// <summary>The chat the message belongs to.</summary>
    [JsonPropertyName("chat")]
    public TelegramChat Chat { get; init; } = new();

    /// <summary>The text content of the message, if any.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// Array of photo sizes sent with the message (Telegram provides multiple resolutions).
    /// The last element is always the largest resolution.
    /// </summary>
    [JsonPropertyName("photo")]
    public TelegramPhotoSize[]? Photo { get; init; }

    /// <summary>Document attachment (files, PDFs, etc.).</summary>
    [JsonPropertyName("document")]
    public TelegramDocument? Document { get; init; }

    /// <summary>Voice message attachment.</summary>
    [JsonPropertyName("voice")]
    public TelegramVoice? Voice { get; init; }

    /// <summary>Audio file attachment (music).</summary>
    [JsonPropertyName("audio")]
    public TelegramAudio? Audio { get; init; }

    /// <summary>Video attachment.</summary>
    [JsonPropertyName("video")]
    public TelegramVideo? Video { get; init; }

    /// <summary>Special entities in the message text (mentions, hashtags, URLs, etc.).</summary>
    [JsonPropertyName("entities")]
    public TelegramEntity[]? Entities { get; init; }

    /// <summary>Caption for media messages (photo, document, video, audio, voice).</summary>
    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    /// <summary>The original message this message is a reply to.</summary>
    [JsonPropertyName("reply_to_message")]
    public TelegramMessage? ReplyToMessage { get; init; }
}