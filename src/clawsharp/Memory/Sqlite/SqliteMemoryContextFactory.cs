using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clawsharp.Memory.Sqlite;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add InitialCreate --context SqliteMemoryContext --project clawsharp/clawsharp.csproj --output-dir Memory/Sqlite/Migrations
/// </summary>
public sealed class SqliteMemoryContextFactory : IDesignTimeDbContextFactory<SqliteMemoryContext>
{
    public SqliteMemoryContext CreateDbContext(string[] args)
    {
        var dbPath = Environment.GetEnvironmentVariable("CLAWSHARP_SQLITE_PATH")
                     ?? Path.Combine(Path.GetTempPath(), "clawsharp-migrations-design.db");

        var optionsBuilder = new DbContextOptionsBuilder<SqliteMemoryContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new SqliteMemoryContext(optionsBuilder.Options);
    }
}