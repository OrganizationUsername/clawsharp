using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a special entity in a Telegram message (mention, hashtag, URL, etc.).</summary>
internal sealed class TelegramEntity
{
    /// <summary>Type of the entity (e.g. "mention", "text_mention", "bot_command").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>Offset in UTF-16 code units to the start of the entity.</summary>
    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    /// <summary>Length of the entity in UTF-16 code units.</summary>
    [JsonPropertyName("length")]
    public int Length { get; init; }

    /// <summary>For "text_mention" entities, the mentioned user.</summary>
    [JsonPropertyName("user")]
    public TelegramUser? User { get; init; }
}