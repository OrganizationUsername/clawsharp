using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// Error object returned by OpenRouter in both HTTP error responses and mid-stream SSE errors.
/// Mid-stream errors arrive with HTTP 200 (headers already sent) and include
/// <c>finish_reason: "error"</c> on the associated choice.
/// </summary>
internal sealed class OpenRouterError
{
    /// <summary>HTTP status code or provider error code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
