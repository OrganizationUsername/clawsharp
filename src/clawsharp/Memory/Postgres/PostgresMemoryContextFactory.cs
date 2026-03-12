using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Memory;

namespace Clawsharp.Memory.Postgres;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add InitialCreate --context PostgresMemoryContext --project clawsharp/clawsharp.csproj --output-dir Memory/Postgres/Migrations
/// Connection string resolution: CLAWSHARP__memory__connectionString env var or ConnectionStrings__DefaultConnection env var.
/// </summary>
public sealed class PostgresMemoryContextFactory : IDesignTimeDbContextFactory<PostgresMemoryContext>
{
    public PostgresMemoryContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CLAWSHARP__memory__connectionString")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string not found. Set environment variable: " +
                "CLAWSHARP__memory__connectionString=\"Host=localhost;Database=clawsharp;Username=clawsharp;Password=...\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<PostgresMemoryContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());

        // Provide a default MemoryConfig for design-time (default embedding dimension 1536)
        var memoryOptions = Options.Create(new MemoryConfig());
        return new PostgresMemoryContext(optionsBuilder.Options, memoryOptions);
    }
}