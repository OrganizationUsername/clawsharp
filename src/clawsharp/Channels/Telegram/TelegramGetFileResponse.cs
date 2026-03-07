using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Response from the Telegram Bot API getFile method.</summary>
internal sealed class TelegramGetFileResponse
{
    /// <summary>Whether the request was successful.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The file information, if the request succeeded.</summary>
    [JsonPropertyName("result")]
    public TelegramFile? Result { get; init; }
}