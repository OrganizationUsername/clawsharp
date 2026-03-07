using Intellenum;

namespace Clawsharp.Cron;

/// <summary>Source identifiers for cron jobs.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class CronSource
{
    /// <summary>Job created dynamically by the agent via CronTool.</summary>
    public static readonly CronSource Agent = new("agent");

    /// <summary>Job seeded from config.json cron entries.</summary>
    public static readonly CronSource Config = new("config");
}