using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Clawsharp.Memory.MsSql;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add MigrationName --context MsSqlMemoryContext --project src/clawsharp/clawsharp.csproj --output-dir Memory/MsSql/Migrations
/// </summary>
public sealed class MsSqlMemoryContextFactory : IDesignTimeDbContextFactory<MsSqlMemoryContext>
{
    public MsSqlMemoryContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<MsSqlMemoryContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration["CLAWSHARP__memory__connectionString"]
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string not found. Set via user secrets or environment variable: " +
                "dotnet user-secrets set \"CLAWSHARP__memory__connectionString\" \"Server=localhost;Database=clawsharp;User Id=sa;Password=...;TrustServerCertificate=True\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MsSqlMemoryContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new MsSqlMemoryContext(optionsBuilder.Options);
    }
}
