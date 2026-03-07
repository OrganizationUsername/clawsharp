using Clawsharp.Config.Security;
namespace Clawsharp.Security;

/// <summary>
/// Landlock LSM filesystem sandboxing configuration (Linux 5.13+ only).
/// When enabled, restricts which paths the gateway process can access
/// at the kernel level. Restrictions are permanent and irreversible.
/// </summary>
public sealed class LandlockConfig
{
    /// <summary>
    /// Enable Landlock LSM process-wide filesystem restrictions.
    /// Default: false (opt-in). Requires Linux kernel 5.13+;
    /// gracefully degrades on older kernels or non-Linux platforms.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Additional paths to allow read-only access (beyond the built-in allowlist).
    /// Supports <c>~/</c> prefix expansion. Useful for NuGet caches or custom data dirs.
    /// </summary>
    public string[]? AdditionalReadPaths { get; init; }

    /// <summary>
    /// Additional paths to allow read+write access.
    /// Supports <c>~/</c> prefix expansion.
    /// </summary>
    public string[]? AdditionalReadWritePaths { get; init; }
}