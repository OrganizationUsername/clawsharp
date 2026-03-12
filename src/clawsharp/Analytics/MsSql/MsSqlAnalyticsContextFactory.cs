using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clawsharp.Analytics.MsSql;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add InitialCreate --context MsSqlAnalyticsContext --project src/clawsharp/clawsharp.csproj --output-dir Analytics/MsSql/Migrations
/// </summary>
public sealed class MsSqlAnalyticsContextFactory : IDesignTimeDbContextFactory<MsSqlAnalyticsContext>
{
    public MsSqlAnalyticsContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CLAWSHARP__analytics__connectionString")
            ?? Environment.GetEnvironmentVariable("CLAWSHARP__memory__connectionString")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string not found. Set environment variable: " +
                "CLAWSHARP__analytics__connectionString=\"Server=localhost;Database=clawsharp;User Id=sa;Password=...;TrustServerCertificate=True\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MsSqlAnalyticsContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new MsSqlAnalyticsContext(optionsBuilder.Options);
    }
}
