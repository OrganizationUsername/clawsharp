namespace Clawsharp.Config.Features;

/// <summary>Configurable size limits for media attachments.</summary>
public sealed class LimitsConfig
{
    /// <summary>Maximum image attachment size in bytes (default: 5 MB).</summary>
    public long MaxImageBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Maximum voice/audio file size in bytes for transcription (default: 25 MB — Whisper API limit).</summary>
    public long MaxVoiceFileBytes { get; init; } = 25 * 1024 * 1024;
}
