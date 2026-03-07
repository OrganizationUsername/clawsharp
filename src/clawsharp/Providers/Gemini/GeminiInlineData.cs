using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Inline binary data for sending images or audio to the Gemini API.</summary>
internal sealed class GeminiInlineData
{
    /// <summary>MIME type of the data (e.g. "image/png", "audio/wav").</summary>
    [JsonPropertyName("mime_type")]
    public string MimeType { get; init; } = "";

    /// <summary>Base64-encoded binary data.</summary>
    [JsonPropertyName("data")]
    public string Data { get; init; } = "";
}