using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a general file sent via Telegram (PDF, ZIP, etc.).</summary>
internal sealed class TelegramDocument
{
    /// <summary>Identifier for this file, which can be used to download it.</summary>
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = "";

    /// <summary>Original filename as defined by sender.</summary>
    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    /// <summary>MIME type of the file.</summary>
    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    /// <summary>File size in bytes.</summary>
    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }
}