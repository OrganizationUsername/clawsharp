using Clawsharp.Security;

namespace Clawsharp.Config.Security;

/// <summary>
///     Configurable network egress policy. When mode is "allowlist", only hosts
///     matching the configured rules are permitted for outbound HTTP connections.
/// </summary>
public sealed class EgressConfig
{
    /// <summary>
    ///     Egress policy mode: "open" (default, all traffic allowed) or "allowlist" (deny-by-default).
    /// </summary>
    public EgressMode Mode { get; init; } = EgressMode.Open;

    /// <summary>
    ///     Rules defining which hosts (and optionally ports) are allowed when mode is "allowlist".
    ///     Supports exact host matches and wildcard prefixes (e.g. "*.example.com").
    /// </summary>
    public List<EgressRule>? Rules { get; init; }
}

/// <summary>
///     A single egress allowlist rule. Matches a specific host or wildcard pattern,
///     optionally restricted to a specific port.
/// </summary>
public sealed class EgressRule
{
    /// <summary>
    ///     Host pattern to match. Exact match (e.g. "api.example.com") or wildcard
    ///     prefix (e.g. "*.example.com" — matches subdomains AND the bare domain).
    /// </summary>
    public string Host { get; init; } = "";

    /// <summary>
    ///     Optional port restriction. Null or 0 means any port is allowed.
    /// </summary>
    public int? Port { get; init; }
}
