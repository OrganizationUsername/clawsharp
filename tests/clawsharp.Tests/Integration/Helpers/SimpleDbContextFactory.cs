using Clawsharp.Analytics.MsSql;
using Clawsharp.Analytics.Postgres;
using Clawsharp.Memory.MsSql;
using Clawsharp.Memory.Postgres;
using Clawsharp.Memory.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Tests.Integration.Helpers;

/// <summary>Minimal IDbContextFactory implementation for integration tests.</summary>
internal sealed class SimpleDbContextFactory<T>(DbContextOptions<T> options) : IDbContextFactory<T>
    where T : DbContext
{
    public T CreateDbContext() => (T)(object)(options switch
    {
        DbContextOptions<SqliteMemoryContext> o => new SqliteMemoryContext(o),
        DbContextOptions<PostgresMemoryContext> o => new PostgresMemoryContext(o),
        DbContextOptions<MsSqlMemoryContext> o => new MsSqlMemoryContext(o),
        DbContextOptions<PostgresAnalyticsContext> o => new PostgresAnalyticsContext(o),
        DbContextOptions<MsSqlAnalyticsContext> o => new MsSqlAnalyticsContext(o),
        _ => throw new NotSupportedException($"No factory mapping for {typeof(T).Name}"),
    });
}