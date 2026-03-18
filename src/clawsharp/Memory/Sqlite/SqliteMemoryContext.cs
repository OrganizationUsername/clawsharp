using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Memory.Sqlite;

[method: RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
[method: RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
public sealed class SqliteMemoryContext(DbContextOptions<SqliteMemoryContext> options) : MemoryDbContextBase(options)
{
    public DbSet<Fact> Facts { get; set; } = null!;

    public DbSet<HistoryEntry> History { get; set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<Iso8601DateTimeOffsetConverter>();

        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<NullableIso8601DateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Fact.Configuration());
        modelBuilder.ApplyConfiguration(new HistoryEntry.Configuration());

        // Embedding property is pgvector-specific; SQLite uses a separate vec0 virtual table
        modelBuilder.Entity<Fact>().Ignore(f => f.Embedding);

        // ISO 8601 "O" format default values — matches the ValueConverter round-trip format.
        // strftime('%f') gives 3 fractional digits; pad with '0000' to match DateTimeOffset.ToString("O")'s 7 digits
        // so lexicographic TEXT comparison stays correct against app-written values.
        modelBuilder.Entity<Fact>()
                    .Property(f => f.CreatedAt)
                    .HasDefaultValueSql("strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'");

        modelBuilder.Entity<HistoryEntry>()
                    .Property(h => h.Ts)
                    .HasDefaultValueSql("strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00'");
    }
}