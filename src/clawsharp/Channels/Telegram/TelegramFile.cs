using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>File information returned by the Telegram Bot API getFile method.</summary>
internal sealed class TelegramFile
{
    /// <summary>Identifier for this file.</summary>
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = "";

    /// <summary>File path for downloading via the Telegram file API.</summary>
    [JsonPropertyName("file_path")]
    public string FilePath { get; init; } = "";
}