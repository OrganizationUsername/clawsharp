using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Response from the Telegram Bot API getMe endpoint.</summary>
internal sealed class TelegramGetMeResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("result")]
    public TelegramBotInfo? Result { get; init; }
}

/// <summary>Bot information returned by getMe.</summary>
internal sealed class TelegramBotInfo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }
}