using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents a video file sent via Telegram.</summary>
internal sealed class TelegramVideo
{
    /// <summary>Identifier for this file, which can be used to download it.</summary>
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = "";

    /// <summary>Video width.</summary>
    [JsonPropertyName("width")]
    public int Width { get; init; }

    /// <summary>Video height.</summary>
    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>Duration of the video in seconds.</summary>
    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    /// <summary>MIME type of the file.</summary>
    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    /// <summary>File size in bytes.</summary>
    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }
}