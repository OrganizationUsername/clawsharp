using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Clawsharp.Analytics.MsSql;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add MigrationName --context MsSqlAnalyticsContext --project src/clawsharp/clawsharp.csproj --output-dir Analytics/MsSql/Migrations
/// </summary>
public sealed class MsSqlAnalyticsContextFactory : IDesignTimeDbContextFactory<MsSqlAnalyticsContext>
{
    public MsSqlAnalyticsContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<MsSqlAnalyticsContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration["CLAWSHARP__analytics__connectionString"]
            ?? configuration["CLAWSHARP__memory__connectionString"]
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string not found. Set via user secrets or environment variable: " +
                "dotnet user-secrets set \"CLAWSHARP__analytics__connectionString\" \"Server=localhost;Database=clawsharp;User Id=sa;Password=...;TrustServerCertificate=True\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MsSqlAnalyticsContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new MsSqlAnalyticsContext(optionsBuilder.Options);
    }
}
