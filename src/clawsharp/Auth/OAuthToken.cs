namespace Clawsharp.Auth;

/// <summary>
/// Persisted OAuth token. For Copilot, <see cref="AccessToken"/> is the short-lived Copilot API token
/// and <see cref="RefreshToken"/> is the long-lived GitHub OAuth token used to obtain new Copilot tokens.
/// </summary>
public sealed class OAuthToken
{
    public string AccessToken { get; init; } = "";

    public string TokenType { get; init; } = "Bearer";

    public string? RefreshToken { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? Scope { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow >= ExpiresAt.Value.AddMinutes(-5);
}