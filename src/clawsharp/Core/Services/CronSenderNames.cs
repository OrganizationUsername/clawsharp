using Intellenum;

namespace Clawsharp.Core.Services;

/// <summary>Well-known sender name identifiers for cron-originated messages.</summary>
[Intellenum<string>]
internal partial class CronSenderName
{
    /// <summary>Messages originating from the cron scheduler.</summary>
    public static readonly CronSenderName Cron = new("cron");
}
