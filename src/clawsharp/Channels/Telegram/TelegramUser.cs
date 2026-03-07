using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a Telegram user.</summary>
internal sealed class TelegramUser
{
    /// <summary>Unique numeric identifier for this user.</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>User's first name.</summary>
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; } = "";

    /// <summary>User's username (without leading @), if set.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }
}