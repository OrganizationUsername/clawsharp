namespace Clawsharp.Tools;

/// <summary>
///     Classification of tool sensitivity for channel-based access control.
///     Non-CLI channels may restrict tools above a configured threshold.
/// </summary>
public enum ToolSensitivity
{
    /// <summary>Read-only, workspace-local operations. Always allowed.</summary>
    Low = 0,

    /// <summary>Write operations within workspace boundaries.</summary>
    Medium = 1,

    /// <summary>Network access, shell execution, external service interaction.</summary>
    High = 2,

    /// <summary>Sub-agent spawning, cron scheduling, persistent effects.</summary>
    Critical = 3,
}
