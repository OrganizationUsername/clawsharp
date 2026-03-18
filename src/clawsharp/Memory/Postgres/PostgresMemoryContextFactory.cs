using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Memory;

namespace Clawsharp.Memory.Postgres;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add MigrationName --context PostgresMemoryContext --project src/clawsharp/clawsharp.csproj --output-dir Memory/Postgres/Migrations
/// </summary>
public sealed class PostgresMemoryContextFactory : IDesignTimeDbContextFactory<PostgresMemoryContext>
{
    public PostgresMemoryContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PostgresMemoryContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration["CLAWSHARP__memory__connectionString"]
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string not found. Set via user secrets or environment variable: " +
                "dotnet user-secrets set \"CLAWSHARP__memory__connectionString\" \"Host=localhost;Database=clawsharp;Username=clawsharp;Password=...\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<PostgresMemoryContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());

        var memoryOptions = Options.Create(new MemoryConfig());
        return new PostgresMemoryContext(optionsBuilder.Options, memoryOptions);
    }
}
