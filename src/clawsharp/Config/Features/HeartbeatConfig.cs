namespace Clawsharp.Config.Features;

/// <summary>Configuration for the periodic heartbeat service.</summary>
public sealed class HeartbeatConfig
{
    /// <summary>Whether the heartbeat service is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    ///     Standard 5-field cron expression (minute hour day-of-month month day-of-week).
    ///     Defaults to every 30 minutes.
    /// </summary>
    public string Schedule { get; init; } = "*/30 * * * *";

    /// <summary>
    ///     Path to the prompt file. Resolved relative to <c>~/.clawsharp/</c> or the current directory.
    /// </summary>
    public string PromptFile { get; init; } = "HEARTBEAT.md";

    /// <summary>
    ///     Which channel's session context to use for the heartbeat message.
    /// </summary>
    public string Channel { get; init; } = "cli";
}