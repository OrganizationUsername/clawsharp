using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Memory;

namespace Clawsharp.Memory.Postgres;

public sealed class PostgresMemoryContext : MemoryDbContextBase
{
    public DbSet<Fact> Facts { get; set; } = null!;

    public DbSet<HistoryEntry> History { get; set; } = null!;

    private readonly int _embeddingDimension;

    [RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
    [RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
    public PostgresMemoryContext(DbContextOptions<PostgresMemoryContext> options, IOptions<MemoryConfig>? memoryConfig = null)
        : base(options)
    {
        _embeddingDimension = memoryConfig?.Value.EmbeddingDimension ?? 1536;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Fact.Configuration());
        modelBuilder.ApplyConfiguration(new HistoryEntry.Configuration());

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Fact>()
                    .Property(f => f.CreatedAt)
                    .HasDefaultValueSql("NOW()");

        // Map the Embedding property to a native pgvector column
        modelBuilder.Entity<Fact>()
                    .Property(f => f.Embedding)
                    .HasColumnType($"vector({_embeddingDimension})")
                    .HasColumnName("embedding_pgvec");

        // HNSW index for fast cosine ANN search
        modelBuilder.Entity<Fact>()
                    .HasIndex(f => f.Embedding)
                    .HasMethod("hnsw")
                    .HasOperators("vector_cosine_ops")
                    .HasStorageParameter("m", 16)
                    .HasStorageParameter("ef_construction", 64);

        modelBuilder.Entity<HistoryEntry>()
                    .Property(h => h.Ts)
                    .HasDefaultValueSql("NOW()");
    }
}