using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clawsharp.Analytics.Postgres;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add InitialCreate --context PostgresAnalyticsContext --project src/clawsharp/clawsharp.csproj --output-dir Analytics/Postgres/Migrations
/// </summary>
public sealed class PostgresAnalyticsContextFactory : IDesignTimeDbContextFactory<PostgresAnalyticsContext>
{
    public PostgresAnalyticsContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CLAWSHARP__analytics__connectionString")
            ?? Environment.GetEnvironmentVariable("CLAWSHARP__memory__connectionString")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string not found. Set environment variable: " +
                "CLAWSHARP__analytics__connectionString=\"Host=localhost;Database=clawsharp;Username=clawsharp;Password=...\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<PostgresAnalyticsContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new PostgresAnalyticsContext(optionsBuilder.Options);
    }
}
