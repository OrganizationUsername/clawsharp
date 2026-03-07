namespace Clawsharp.Security;

/// <summary>Available shell sandbox backends.</summary>
public enum SandboxBackend
{
    /// <summary>No sandboxing. Process runs directly.</summary>
    None,

    /// <summary>Firejail sandbox (Linux only). Requires firejail to be installed.</summary>
    Firejail,

    /// <summary>Docker container sandbox. Requires Docker to be installed.</summary>
    Docker,

    /// <summary>Bubblewrap sandbox (Linux only). Requires bwrap to be installed.</summary>
    Bubblewrap,

    /// <summary>Auto-detect: tries Bubblewrap, Firejail, Docker (in order) on Linux; Docker-only on other platforms; falls back to None.</summary>
    Auto,
}