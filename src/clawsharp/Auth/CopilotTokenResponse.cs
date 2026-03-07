using System.Text.Json.Serialization;

namespace Clawsharp.Auth;

/// <summary>
/// Response from GET https://api.github.com/copilot_internal/v2/token.
/// Contains a short-lived Copilot API token (~30 min expiry).
/// </summary>
internal sealed class CopilotTokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; init; }
}