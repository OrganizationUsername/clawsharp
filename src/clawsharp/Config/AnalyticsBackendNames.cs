using Intellenum;

namespace Clawsharp.Config;

/// <summary>Well-known analytics storage backend identifiers.</summary>
[Intellenum<string>]
internal partial class AnalyticsBackend
{
    public static readonly AnalyticsBackend Postgres = new("postgres");
    public static readonly AnalyticsBackend MsSql = new("mssql");
    public static readonly AnalyticsBackend Sqlite = new("sqlite");
}
