using Clawsharp.Core.Pipeline;
using Clawsharp.Tools.Ops;

namespace Clawsharp.Core.Services;

/// <summary>
///     Tracks whether the current async flow is executing within a cron job context.
///     Used to prevent cron jobs from scheduling new cron jobs (infinite loop prevention).
/// </summary>
internal static class CronContext
{
    private static readonly AsyncLocal<bool> _isInCronExecution = new();

    /// <summary>
    ///     Gets or sets whether the current async flow is inside a cron job execution.
    ///     Set by <see cref="AgentLoop" /> when processing a cron-fired message;
    ///     checked by <see cref="CronTool" /> to block self-scheduling.
    /// </summary>
    public static bool IsInCronExecution
    {
        get => _isInCronExecution.Value;
        set => _isInCronExecution.Value = value;
    }
}