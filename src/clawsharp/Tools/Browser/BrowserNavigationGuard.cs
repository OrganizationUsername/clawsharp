namespace Clawsharp.Tools.Browser;

/// <summary>
/// Shared domain-allowlist checking logic for browser automation tools
/// (<see cref="BrowserTool"/> and <see cref="PinchTabTool"/>).
/// Returns an error string if the domain is blocked, or <c>null</c> if navigation is allowed.
/// </summary>
internal static class BrowserNavigationGuard
{
    /// <summary>
    /// Checks whether the given URI's host is in the configured allowed domains list.
    /// </summary>
    /// <param name="uri">The URI to check.</param>
    /// <param name="allowedDomains">
    ///     The configured domain allowlist. If <c>null</c> or empty, all domains are allowed.
    /// </param>
    /// <returns>An error message string if the domain is blocked; <c>null</c> if allowed.</returns>
    public static string? CheckAllowedDomain(Uri uri, string[]? allowedDomains)
    {
        if (allowedDomains is not { Length: > 0 })
        {
            return null; // No restriction
        }

        var host = uri.Host;
        foreach (var domain in allowedDomains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return null; // Allowed
            }
        }

        return $"Error: domain '{host}' is not in the allowed domains list. " +
               $"Allowed: {string.Join(", ", allowedDomains)}";
    }
}