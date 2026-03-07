using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a Telegram chat (private, group, supergroup, or channel).</summary>
internal sealed class TelegramChat
{
    /// <summary>Unique numeric identifier for this chat.</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>True if the supergroup has topics enabled (forum mode).</summary>
    [JsonPropertyName("is_forum")]
    public bool? IsForum { get; init; }
}