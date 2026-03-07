using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Response from the Telegram Bot API sendChatAction method.</summary>
internal sealed class TelegramSendChatActionResponse
{
    /// <summary>Whether the request was successful.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }
}