using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Represents one size variant of a Telegram photo attachment.</summary>
internal sealed class TelegramPhotoSize
{
    /// <summary>Identifier for this file, which can be used to download or reuse the file.</summary>
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = "";

    /// <summary>Photo width in pixels.</summary>
    [JsonPropertyName("width")]
    public int Width { get; init; }

    /// <summary>Photo height in pixels.</summary>
    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>File size in bytes, if known.</summary>
    [JsonPropertyName("file_size")]
    public int FileSize { get; init; }
}