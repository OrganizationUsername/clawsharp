using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Response from the Telegram Bot API sendMessage method.</summary>
internal sealed class TelegramSendMessageResponse
{
    /// <summary>Whether the request was successful.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The sent message, if the request succeeded.</summary>
    [JsonPropertyName("result")]
    public TelegramMessage? Result { get; init; }
}