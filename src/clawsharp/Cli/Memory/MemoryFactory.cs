using System.Diagnostics.CodeAnalysis;
using Clawsharp.Config;
using Clawsharp.Memory;
using Clawsharp.Memory.Markdown;
using Clawsharp.Memory.MsSql;
using Clawsharp.Memory.Postgres;
using Clawsharp.Memory.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Clawsharp.Config.Memory;

namespace Clawsharp.Cli.Memory;

/// <summary>
///     Creates an <see cref="IMemory"/> instance outside of DI, using the same backend
///     selection logic as <see cref="GatewayHost"/>.
/// </summary>
internal static class MemoryFactory
{
    [RequiresUnreferencedCode("Creates EF Core DbContext instances which use reflection for model building.")]
    [RequiresDynamicCode("Creates EF Core DbContext instances which require dynamic code for query compilation.")]
    public static IMemory Create(AppConfig config)
    {
        var backend = MemoryBackend.TryFromValue(config.Memory.Backend, out var mb) ? mb : MemoryBackend.Markdown;

        if (backend == MemoryBackend.Postgres)
        {
            var cs = config.Memory.ConnectionString
                     ?? throw new InvalidOperationException("Postgres memory requires a connection string.");
            var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                          .UseNpgsql(cs).Options;
            var factory = new FuncDbContextFactory<PostgresMemoryContext>(() => new PostgresMemoryContext(options));
            return new PostgresMemory(factory, NullLogger<PostgresMemory>.Instance);
        }

        if (backend == MemoryBackend.MsSql)
        {
            var cs = config.Memory.ConnectionString
                     ?? throw new InvalidOperationException("MSSQL memory requires a connection string.");
            var options = new DbContextOptionsBuilder<MsSqlMemoryContext>()
                          .UseSqlServer(cs).Options;
            var factory = new FuncDbContextFactory<MsSqlMemoryContext>(() => new MsSqlMemoryContext(options));
            return new MsSqlMemory(factory, NullLogger<MsSqlMemory>.Instance);
        }

        if (backend == MemoryBackend.Sqlite)
        {
            var dir = ConfigLoader.ExpandHome(config.Memory.Dir);
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, "memory.db");
            var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                          .UseSqlite($"Data Source={dbPath}").Options;
            var factory = new FuncDbContextFactory<SqliteMemoryContext>(() => new SqliteMemoryContext(options));
            return new SqliteMemory(factory, NullLogger<SqliteMemory>.Instance);
        }

        // Default: markdown
        var mdDir = ConfigLoader.ExpandHome(config.Memory.Dir);
        Directory.CreateDirectory(mdDir);
        return new MarkdownMemory(mdDir);
    }

    /// <summary>
    ///     AOT-safe <see cref="IDbContextFactory{TContext}"/> for CLI use outside the DI host.
    ///     Uses an explicit factory delegate instead of <c>Activator.CreateInstance</c> so the
    ///     AOT compiler can see the constructor call.
    /// </summary>
    private sealed class FuncDbContextFactory<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors |
            DynamicallyAccessedMemberTypes.PublicProperties)]
        TContext>(Func<TContext> create)
        : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => create();
    }
}