using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clawsharp.Analytics.Sqlite;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add InitialCreate --context SqliteAnalyticsContext --project src/clawsharp/clawsharp.csproj --output-dir Analytics/Sqlite/Migrations
/// </summary>
public sealed class SqliteAnalyticsContextFactory : IDesignTimeDbContextFactory<SqliteAnalyticsContext>
{
    public SqliteAnalyticsContext CreateDbContext(string[] args)
    {
        var dbPath = Environment.GetEnvironmentVariable("CLAWSHARP_SQLITE_PATH")
                     ?? Path.Combine(Path.GetTempPath(), "clawsharp-analytics-migrations-design.db");

        var optionsBuilder = new DbContextOptionsBuilder<SqliteAnalyticsContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new SqliteAnalyticsContext(optionsBuilder.Options);
    }
}
