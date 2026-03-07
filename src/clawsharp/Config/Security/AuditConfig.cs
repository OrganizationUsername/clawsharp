using System.ComponentModel.DataAnnotations;

namespace Clawsharp.Config.Security;

/// <summary>Audit logging configuration.</summary>
public sealed class AuditConfig
{
    /// <summary>Enable audit logging. Enabled by default.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Path to the audit log file. Relative paths are resolved under ~/.clawsharp/.
    /// Defaults to "audit.log" (-> ~/.clawsharp/audit.log).
    /// </summary>
    public string LogPath { get; init; } = "audit.log";

    /// <summary>Maximum audit log file size in megabytes before rotation. Default: 50 MB.</summary>
    [Range(1, 10000)]
    public int MaxSizeMb { get; init; } = 50;

    /// <summary>Number of days to retain rotated audit log files. Default: 90 days.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; init; } = 90;
}