using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clawsharp.Memory.MsSql;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add InitialCreate --context MsSqlMemoryContext --project clawsharp/clawsharp.csproj --output-dir Memory/MsSql/Migrations
/// Connection string resolution: CLAWSHARP__memory__connectionString env var or ConnectionStrings__DefaultConnection env var.
/// </summary>
public sealed class MsSqlMemoryContextFactory : IDesignTimeDbContextFactory<MsSqlMemoryContext>
{
    public MsSqlMemoryContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CLAWSHARP__memory__connectionString")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string not found. Set environment variable: " +
                "CLAWSHARP__memory__connectionString=\"Server=localhost;Database=clawsharp;User Id=sa;Password=...;TrustServerCertificate=True\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MsSqlMemoryContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new MsSqlMemoryContext(optionsBuilder.Options);
    }
}