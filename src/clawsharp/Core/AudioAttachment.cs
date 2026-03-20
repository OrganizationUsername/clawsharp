using System.Text.Json.Serialization;

namespace Clawsharp.Core;

/// <summary>
/// An audio attachment sent by the user for native model processing.
/// Carries raw audio data rather than a transcription.
/// </summary>
public sealed record AudioAttachment
{
    /// <summary>Base64-encoded audio data.</summary>
    public required string Base64Data { get; init; }

    /// <summary>Audio format identifier (e.g. "wav", "mp3", "ogg", "flac", "m4a").</summary>
    public required string Format { get; init; }

    /// <summary>
    /// Internal constructor for JSON deserialization within the assembly.
    /// External callers should use <see cref="Create"/> or <see cref="FromBase64"/>.
    /// </summary>
    [JsonConstructor]
    internal AudioAttachment()
    {
    }

    /// <summary>Creates an AudioAttachment from raw bytes and MIME type.</summary>
    /// <param name="audioBytes">Raw audio bytes.</param>
    /// <param name="mimeType">IANA media type (e.g. "audio/ogg").</param>
    /// <param name="maxAudioBytes">Maximum allowed audio size in bytes (default: 25 MB).</param>
    public static AudioAttachment Create(byte[] audioBytes, string mimeType, long maxAudioBytes = 25 * 1024 * 1024)
    {
        if (audioBytes.Length > maxAudioBytes)
        {
            throw new ArgumentException(
                $"Audio size ({audioBytes.Length / (1024 * 1024)} MB) exceeds the {maxAudioBytes / (1024 * 1024)} MB limit.",
                nameof(audioBytes));
        }

        var base64 = Convert.ToBase64String(audioBytes);
        var format = MimeToFormat(mimeType);
        return new AudioAttachment { Base64Data = base64, Format = format };
    }

    /// <summary>Creates an AudioAttachment from already-encoded base64 data.</summary>
    public static AudioAttachment FromBase64(string base64, string format)
    {
        return new AudioAttachment { Base64Data = base64, Format = format };
    }

    /// <summary>Converts a MIME type to an audio format identifier.</summary>
    internal static string MimeToFormat(string mimeType)
    {
        // Strip charset/encoding params (e.g. "audio/ogg; codecs=opus") without allocating via Split.
        var span = mimeType.AsSpan();
        var semicolonIdx = span.IndexOf(';');
        if (semicolonIdx >= 0)
        {
            span = span[..semicolonIdx];
        }

        span = span.Trim();

        if (span.Equals("audio/wav", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("audio/x-wav", StringComparison.OrdinalIgnoreCase)) return "wav";
        if (span.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase)) return "mp3";
        if (span.Equals("audio/ogg", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("application/ogg", StringComparison.OrdinalIgnoreCase)) return "ogg";
        if (span.Equals("audio/flac", StringComparison.OrdinalIgnoreCase)) return "flac";
        if (span.Equals("audio/aac", StringComparison.OrdinalIgnoreCase)) return "aac";
        if (span.Equals("audio/mp4", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("audio/m4a", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("audio/x-m4a", StringComparison.OrdinalIgnoreCase)) return "m4a";
        if (span.Equals("audio/webm", StringComparison.OrdinalIgnoreCase)) return "webm";
        if (span.Equals("audio/aiff", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("audio/x-aiff", StringComparison.OrdinalIgnoreCase)) return "aiff";

        return "wav"; // safe default
    }

    /// <summary>Maps an audio format identifier to a file extension.</summary>
    internal static string FormatToExtension(string format) => format switch
    {
        "mp3" => ".mp3",
        "ogg" => ".ogg",
        "flac" => ".flac",
        "aac" => ".aac",
        "m4a" => ".m4a",
        "webm" => ".webm",
        "aiff" => ".aiff",
        "opus" => ".opus",
        "pcm16" => ".pcm",
        _ => ".wav"
    };
}
