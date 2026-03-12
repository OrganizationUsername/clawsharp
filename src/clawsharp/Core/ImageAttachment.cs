using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Clawsharp.Core;

/// <summary>
/// A single image attachment carried alongside a message.
/// Base64Data is the raw base-64 encoded image bytes; MimeType is the IANA media type
/// (e.g. "image/jpeg", "image/png").
/// Use <see cref="Create"/> for validated construction; the constructor is internal
/// to allow JSON deserialization within the assembly while preventing unvalidated
/// external instantiation.
/// </summary>
public sealed record ImageAttachment
{
    /// <summary>Base64-encoded image data.</summary>
    public required string Base64Data { get; init; }

    /// <summary>IANA media type (e.g. "image/jpeg").</summary>
    public required string MimeType { get; init; }

    /// <summary>Allowed MIME types for image attachments.</summary>
    private static readonly FrozenSet<string> AllowedMimeTypes = FrozenSet.ToFrozenSet(
        [
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp"
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Internal constructor for JSON deserialization within the assembly.
    /// External callers should use <see cref="Create"/> for validated construction.
    /// </summary>
    [JsonConstructor]
    internal ImageAttachment()
    {
    }

    /// <summary>
    /// Creates a validated <see cref="ImageAttachment"/>.
    /// Validates that the decoded image size does not exceed <paramref name="maxImageBytes"/>
    /// and that the MIME type is in the allowlist.
    /// </summary>
    /// <param name="base64">Base64-encoded image data.</param>
    /// <param name="mime">IANA media type (e.g. "image/jpeg").</param>
    /// <param name="maxImageBytes">Maximum allowed image size in bytes (default: 5 MB).</param>
    /// <returns>A validated <see cref="ImageAttachment"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the decoded size exceeds the limit or the MIME type is not allowed.
    /// </exception>
    public static ImageAttachment Create(string base64, string mime, long maxImageBytes = 5 * 1024 * 1024)
    {
        // Fast upper-bound estimate of decoded byte count without allocating a full decode.
        // base64 encodes 3 bytes per 4 characters; padding may reduce actual size by 1-2 bytes,
        // so this is a safe (slightly over-counting) estimate.
        var estimatedBytes = base64.Length * 3 / 4;
        if (estimatedBytes > maxImageBytes)
        {
            throw new ArgumentException(
                $"Image size (~{estimatedBytes / (1024 * 1024)} MB) exceeds the {maxImageBytes / (1024 * 1024)} MB limit.",
                nameof(base64));
        }

        if (!AllowedMimeTypes.Contains(mime))
        {
            throw new ArgumentException(
                $"MIME type '{mime}' is not allowed. Allowed types: {string.Join(", ", AllowedMimeTypes)}.",
                nameof(mime));
        }

        return new ImageAttachment { Base64Data = base64, MimeType = mime };
    }
}