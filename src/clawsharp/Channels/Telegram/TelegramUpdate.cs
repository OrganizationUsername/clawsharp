using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a single update event from the Telegram Bot API.</summary>
internal sealed class TelegramUpdate
{
    /// <summary>The unique identifier for this update.</summary>
    [JsonPropertyName("update_id")]
    public long UpdateId { get; init; }

    /// <summary>The message associated with this update, if any.</summary>
    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; init; }

    /// <summary>Callback query from an inline keyboard button press.</summary>
    [JsonPropertyName("callback_query")]
    public TelegramCallbackQuery? CallbackQuery { get; init; }
}