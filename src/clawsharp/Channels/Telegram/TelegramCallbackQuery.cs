using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a callback query from an inline keyboard button press.</summary>
internal sealed class TelegramCallbackQuery
{
    /// <summary>Unique identifier for this query.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Sender of the callback query.</summary>
    [JsonPropertyName("from")]
    public TelegramUser From { get; init; } = new();

    /// <summary>Message with the callback button that originated the query.</summary>
    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; init; }

    /// <summary>Data associated with the callback button.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>Identifier of the message sent via the bot in inline mode.</summary>
    [JsonPropertyName("inline_message_id")]
    public string? InlineMessageId { get; init; }
}