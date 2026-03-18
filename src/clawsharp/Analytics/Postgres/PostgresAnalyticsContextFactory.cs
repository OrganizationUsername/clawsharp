using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Clawsharp.Analytics.Postgres;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add MigrationName --context PostgresAnalyticsContext --project src/clawsharp/clawsharp.csproj --output-dir Analytics/Postgres/Migrations
/// </summary>
public sealed class PostgresAnalyticsContextFactory : IDesignTimeDbContextFactory<PostgresAnalyticsContext>
{
    public PostgresAnalyticsContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PostgresAnalyticsContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration["CLAWSHARP__analytics__connectionString"]
            ?? configuration["CLAWSHARP__memory__connectionString"]
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string not found. Set via user secrets or environment variable: " +
                "dotnet user-secrets set \"CLAWSHARP__analytics__connectionString\" \"Host=localhost;Database=clawsharp;Username=clawsharp;Password=...\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<PostgresAnalyticsContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new PostgresAnalyticsContext(optionsBuilder.Options);
    }
}
