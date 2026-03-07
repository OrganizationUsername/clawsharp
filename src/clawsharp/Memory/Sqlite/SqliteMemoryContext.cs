using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Memory.Sqlite;

public sealed class SqliteMemoryContext : MemoryDbContextBase
{
    public DbSet<Fact> Facts { get; set; } = null!;

    public DbSet<HistoryEntry> History { get; set; } = null!;

    [RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
    [RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
    public SqliteMemoryContext(DbContextOptions<SqliteMemoryContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Fact.Configuration());
        modelBuilder.ApplyConfiguration(new HistoryEntry.Configuration());

        // Embedding property is pgvector-specific; SQLite uses a separate vec0 virtual table
        modelBuilder.Entity<Fact>().Ignore(f => f.Embedding);

        modelBuilder.Entity<Fact>()
                    .Property(f => f.CreatedAt)
                    .HasDefaultValueSql("datetime('now')");

        modelBuilder.Entity<HistoryEntry>()
                    .Property(h => h.Ts)
                    .HasDefaultValueSql("datetime('now')");
    }
}