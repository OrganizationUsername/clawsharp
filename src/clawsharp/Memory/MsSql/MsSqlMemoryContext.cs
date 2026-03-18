using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Memory.MsSql;

[method: RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
[method: RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
public sealed class MsSqlMemoryContext(DbContextOptions<MsSqlMemoryContext> options) : MemoryDbContextBase(options)
{
    public DbSet<Fact> Facts { get; set; } = null!;

    public DbSet<HistoryEntry> History { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Fact.Configuration());
        modelBuilder.ApplyConfiguration(new HistoryEntry.Configuration());

        // Embedding property is pgvector-specific; MsSql uses in-process cosine scoring
        modelBuilder.Entity<Fact>().Ignore(f => f.Embedding);

        modelBuilder.Entity<Fact>()
                    .Property(f => f.CreatedAt)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        modelBuilder.Entity<HistoryEntry>()
                    .Property(h => h.Ts)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()");
    }
}