using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Error details returned by the Gemini API.</summary>
internal sealed class GeminiApiError
{
    /// <summary>HTTP status code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    /// <summary>Error status string (e.g. "INVALID_ARGUMENT").</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}