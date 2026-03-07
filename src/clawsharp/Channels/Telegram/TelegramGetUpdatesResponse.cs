using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Response from the Telegram Bot API getUpdates method.</summary>
internal sealed class TelegramGetUpdatesResponse
{
    /// <summary>Whether the request was successful.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>Human-readable description of the error (present when <see cref="Ok"/> is false).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>HTTP-like error code from the Telegram API (present when <see cref="Ok"/> is false).</summary>
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>List of update events received.</summary>
    [JsonPropertyName("result")]
    public List<TelegramUpdate> Result { get; init; } = [];
}