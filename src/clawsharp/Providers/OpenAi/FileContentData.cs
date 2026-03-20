using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>File data for a file content part in the messages array.</summary>
internal sealed class FileContentData
{
    /// <summary>Original filename.</summary>
    [JsonPropertyName("filename")]
    public string Filename { get; init; } = "";

    /// <summary>File data as a data: URL (e.g. "data:application/pdf;base64,...").</summary>
    [JsonPropertyName("file_data")]
    public string FileData { get; init; } = "";
}
