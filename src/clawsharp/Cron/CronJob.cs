namespace Clawsharp.Cron;

/// <summary>Represents a scheduled cron job.</summary>
public sealed class CronJob
{
    /// <summary>Unique job identifier.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Optional human-readable name for the job.</summary>
    public string? Name { get; init; }

    /// <summary>Schedule kind: "cron", "every", or "at".</summary>
    public CronScheduleKind ScheduleKind { get; init; } = CronScheduleKind.Cron;

    /// <summary>Schedule expression: cron=5-field expr, every=ms as string, at=ISO 8601 datetime.</summary>
    public string ScheduleExpr { get; init; } = "";

    /// <summary>Optional timezone identifier.</summary>
    public string? Tz { get; init; }

    /// <summary>Target channel name.</summary>
    public string Channel { get; init; } = "cli";

    /// <summary>Prompt text injected as a user message on schedule.</summary>
    public string Message { get; init; } = "";

    /// <summary>Sender ID used for session scoping.</summary>
    public string SenderId { get; init; } = "cron";

    /// <summary>Whether the job is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ISO 8601 timestamp when the job was created.</summary>
    public string CreatedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");

    /// <summary>ISO 8601 timestamp of the last successful run.</summary>
    public string? LastRunAt { get; set; }

    /// <summary>Total number of times this job has been executed.</summary>
    public int RunCount { get; set; }

    /// <summary>Job source: "agent" (created by CronTool) or "config" (seeded from config.json).</summary>
    public CronSource Source { get; init; } = CronSource.Agent;

    /// <summary>Optional model override. When set, the agent loop uses this model instead of the default.</summary>
    public string? Model { get; init; }

    /// <summary>Optional provider override. When set, the agent loop uses this provider instead of the default.</summary>
    public string? Provider { get; init; }
}