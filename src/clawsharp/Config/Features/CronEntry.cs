namespace Clawsharp.Config.Features;

/// <summary>A cron job entry defined in configuration.</summary>
public sealed class CronEntry
{
    /// <summary>Standard 5-field cron expression: minute hour day-of-month month day-of-week.</summary>
    public string Schedule { get; init; } = "";

    /// <summary>Target channel name (cli, telegram, discord, etc.).</summary>
    public string Channel { get; init; } = "cli";

    /// <summary>Prompt text injected as a user message on schedule.</summary>
    public string Message { get; init; } = "";

    /// <summary>SenderId used for session scoping. Defaults to "cron".</summary>
    public string SenderId { get; init; } = "cron";

    /// <summary>Optional model override. When set, the agent loop uses this model instead of the default.</summary>
    public string? Model { get; init; }

    /// <summary>Optional provider override. When set, the agent loop uses this provider instead of the default.</summary>
    public string? Provider { get; init; }
}