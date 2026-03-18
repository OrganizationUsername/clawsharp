using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Clawsharp.Memory.Sqlite;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Usage: dotnet ef migrations add MigrationName --context SqliteMemoryContext --project src/clawsharp/clawsharp.csproj --output-dir Memory/Sqlite/Migrations
/// </summary>
public sealed class SqliteMemoryContextFactory : IDesignTimeDbContextFactory<SqliteMemoryContext>
{
    public SqliteMemoryContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<SqliteMemoryContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var dbPath = configuration["CLAWSHARP_SQLITE_PATH"]
                     ?? Path.Combine(Path.GetTempPath(), "clawsharp-migrations-design.db");

        var optionsBuilder = new DbContextOptionsBuilder<SqliteMemoryContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new SqliteMemoryContext(optionsBuilder.Options);
    }
}
