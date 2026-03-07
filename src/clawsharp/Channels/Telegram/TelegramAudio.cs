using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents an audio file (music) sent via Telegram.</summary>
internal sealed class TelegramAudio
{
    /// <summary>Identifier for this file, which can be used to download it.</summary>
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = "";

    /// <summary>Duration of the audio in seconds.</summary>
    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    /// <summary>Title of the audio track.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Performer of the audio track.</summary>
    [JsonPropertyName("performer")]
    public string? Performer { get; init; }

    /// <summary>MIME type of the file.</summary>
    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    /// <summary>File size in bytes.</summary>
    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }
}