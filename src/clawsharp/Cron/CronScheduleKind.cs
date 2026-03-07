using Intellenum;

namespace Clawsharp.Cron;

/// <summary>Schedule kind identifiers for cron jobs.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class CronScheduleKind
{
    /// <summary>Standard 5-field cron expression.</summary>
    public static readonly CronScheduleKind Cron = new("cron");

    /// <summary>Interval-based schedule (e.g. "every:5m").</summary>
    public static readonly CronScheduleKind Every = new("every");

    /// <summary>One-shot ISO 8601 timestamp (e.g. "at:2026-03-15T09:00:00Z").</summary>
    public static readonly CronScheduleKind At = new("at");
}