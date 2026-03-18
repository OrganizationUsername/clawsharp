using Clawsharp.Security;

namespace Clawsharp.Config.Security;

/// <summary>Security-related configuration.</summary>
public sealed class SecurityConfig
{
    /// <summary>SSRF prevention settings.</summary>
    public SsrfConfig? Ssrf { get; init; }

    /// <summary>Prompt injection guard settings.</summary>
    public PromptGuardConfig? PromptGuard { get; init; }

    /// <summary>LLM output leak detection settings.</summary>
    public LeakDetectorConfig? LeakDetector { get; init; }

    /// <summary>Landlock LSM process sandboxing (Linux 5.13+ only, opt-in).</summary>
    public LandlockConfig Landlock { get; init; } = new();

    /// <summary>Canary token exfiltration guard settings.</summary>
    public CanaryGuardConfig? CanaryGuard { get; init; }

    /// <summary>
    ///     Regex patterns for commands that require explicit approval even when tools are auto-approved.
    ///     Matched against the raw command string. Built-in patterns are always applied;
    ///     these are additional user-defined patterns.
    /// </summary>
    public List<string>? RequireApprovalPatterns { get; init; }

    /// <summary>
    ///     Regex patterns for commands that are pre-approved, overriding both built-in and
    ///     custom <see cref="RequireApprovalPatterns" />. Use to selectively allow specific
    ///     commands that would otherwise require approval.
    /// </summary>
    public List<string>? AutoApprovePatterns { get; init; }

    /// <summary>
    ///     Maximum tool sensitivity allowed on non-CLI channels.
    ///     Tools above this level are blocked with an explanation.
    ///     Default: "high" — blocks only Critical tools on non-CLI channels.
    ///     Options: "low", "medium", "high", "critical" (or "unrestricted").
    /// </summary>
    public string MaxNonCliToolSensitivity { get; init; } = "high";

    /// <summary>
    ///     Allowed external domains for network tools (web_fetch, browser).
    ///     If null, all domains are allowed (default — open access).
    ///     If set to an empty list, ALL external domains are blocked.
    ///     If set to a non-empty list, only those domains (and their subdomains) are allowed.
    ///     Follows AllowFrom semantics: null=allow-all, []=deny-all, entries=allowlist.
    /// </summary>
    public List<string>? AllowedExternalDomains { get; init; }
}

/// <summary>Canary token exfiltration guard configuration.</summary>
public sealed class CanaryGuardConfig
{
    /// <summary>Enable canary token injection and exfiltration detection. Enabled by default.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>SSRF prevention settings.</summary>
public sealed class SsrfConfig
{
    /// <summary>Enable SSRF checks on outbound HTTP requests. Enabled by default.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Prompt injection guard configuration.</summary>
public sealed class PromptGuardConfig
{
    /// <summary>
    /// Action on injection detection: "warn" (default), "block", or "sanitize".
    /// "warn": log and allow through. "block": reject with error reply.
    /// "sanitize": replace matched text with [FILTERED] and forward.
    /// </summary>
    public PromptGuardMode Mode { get; set; } = PromptGuardMode.Warn;

    /// <summary>Custom patterns to scan for (appended to built-in patterns).</summary>
    public List<string>? CustomPatterns { get; init; }
}

/// <summary>LLM output leak detection configuration.</summary>
public sealed class LeakDetectorConfig
{
    /// <summary>
    /// Detection sensitivity (0.0–1.0, default 0.7).
    /// At 0.0: only structural patterns (API keys, AWS, JWTs, private keys, DB URLs).
    /// Above 0.5: also generic secrets (password=, token=) and high-entropy tokens.
    /// Set to 0 to disable leak detection entirely.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Range(0.0, 1.0)]
    public double Sensitivity { get; init; } = 0.7;
}