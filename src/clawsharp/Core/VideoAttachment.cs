using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Clawsharp.Core;

/// <summary>
/// A video attachment carried alongside a message.
/// Use <see cref="Create"/> for validated construction.
/// </summary>
public sealed record VideoAttachment
{
    /// <summary>Base64-encoded video data.</summary>
    public required string Base64Data { get; init; }

    /// <summary>IANA media type (e.g. "video/mp4").</summary>
    public required string MimeType { get; init; }

    /// <summary>Allowed MIME types for video attachments.</summary>
    private static readonly FrozenSet<string> AllowedMimeTypes = FrozenSet.ToFrozenSet(
    [
        "video/mp4",
        "video/mpeg",
        "video/quicktime", // .mov
        "video/webm"
    ],
    StringComparer.OrdinalIgnoreCase);

    [JsonConstructor]
    internal VideoAttachment() { }

    /// <summary>Checks whether a MIME type is allowed for video attachments.</summary>
    public static bool IsAllowedMimeType(string mime) => AllowedMimeTypes.Contains(mime);

    /// <summary>
    /// Creates a validated <see cref="VideoAttachment"/>.
    /// </summary>
    public static VideoAttachment Create(string base64, string mime, long maxVideoBytes = 100 * 1024 * 1024)
    {
        var estimatedBytes = base64.Length * 3 / 4;
        if (estimatedBytes > maxVideoBytes)
        {
            throw new ArgumentException(
                $"Video size (~{estimatedBytes / (1024 * 1024)} MB) exceeds the {maxVideoBytes / (1024 * 1024)} MB limit.",
                nameof(base64));
        }

        if (!AllowedMimeTypes.Contains(mime))
        {
            throw new ArgumentException(
                $"MIME type '{mime}' is not allowed. Allowed types: {string.Join(", ", AllowedMimeTypes)}.",
                nameof(mime));
        }

        return new VideoAttachment { Base64Data = base64, MimeType = mime };
    }
}
