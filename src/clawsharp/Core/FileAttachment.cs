using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Clawsharp.Core;

/// <summary>
/// A file attachment (PDF, text, etc.) sent by the user.
/// Base64Data is the raw base-64 encoded file bytes; MimeType is the IANA media type
/// (e.g. "application/pdf", "text/plain").
/// Use <see cref="Create"/> for validated construction; the constructor is internal
/// to allow JSON deserialization within the assembly while preventing unvalidated
/// external instantiation.
/// </summary>
public sealed record FileAttachment
{
    /// <summary>Base64-encoded file data.</summary>
    public required string Base64Data { get; init; }

    /// <summary>IANA media type (e.g. "application/pdf").</summary>
    public required string MimeType { get; init; }

    /// <summary>Original filename.</summary>
    public required string Filename { get; init; }

    /// <summary>Allowed MIME types for file attachments.</summary>
    private static readonly FrozenSet<string> AllowedMimeTypes = FrozenSet.ToFrozenSet(
    [
        "application/pdf",
        "text/plain",
        "text/csv",
        "text/markdown",
        "application/json",
        "text/html",
        "text/xml",
        "application/xml"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Internal constructor for JSON deserialization within the assembly.
    /// External callers should use <see cref="Create"/> for validated construction.
    /// </summary>
    [JsonConstructor]
    internal FileAttachment()
    {
    }

    /// <summary>
    /// Creates a validated <see cref="FileAttachment"/>.
    /// Validates that the decoded file size does not exceed <paramref name="maxFileBytes"/>
    /// and that the MIME type is in the allowlist.
    /// </summary>
    /// <param name="base64">Base64-encoded file data.</param>
    /// <param name="mime">IANA media type (e.g. "application/pdf").</param>
    /// <param name="filename">Original filename.</param>
    /// <param name="maxFileBytes">Maximum allowed file size in bytes (default: 20 MB).</param>
    /// <returns>A validated <see cref="FileAttachment"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the decoded size exceeds the limit or the MIME type is not allowed.
    /// </exception>
    public static FileAttachment Create(string base64, string mime, string filename, long maxFileBytes = 20 * 1024 * 1024)
    {
        // Fast upper-bound estimate of decoded byte count without allocating a full decode.
        // base64 encodes 3 bytes per 4 characters; padding may reduce actual size by 1-2 bytes,
        // so this is a safe (slightly over-counting) estimate.
        var estimatedBytes = base64.Length * 3 / 4;
        if (estimatedBytes > maxFileBytes)
        {
            throw new ArgumentException(
                $"File size (~{estimatedBytes / (1024 * 1024)} MB) exceeds the {maxFileBytes / (1024 * 1024)} MB limit.",
                nameof(base64));
        }

        if (!AllowedMimeTypes.Contains(mime))
        {
            throw new ArgumentException(
                $"MIME type '{mime}' is not allowed. Allowed types: {string.Join(", ", AllowedMimeTypes)}.",
                nameof(mime));
        }

        return new FileAttachment { Base64Data = base64, MimeType = mime, Filename = filename };
    }

    /// <summary>
    /// Checks whether a MIME type is in the allowlist for file attachments.
    /// </summary>
    public static bool IsAllowedMimeType(string? mimeType)
    {
        if (mimeType is null) return false;
        return AllowedMimeTypes.Contains(mimeType);
    }
}
