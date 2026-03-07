using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace Clawsharp.Memory.Entities;

/// <summary>An individual remembered fact.</summary>
public sealed class Fact
{
    // Schema name constants — single source of truth for EF Core configuration and raw SQL.
    // Table name matches the DbSet property name (Facts); column names match C# property names.
    public const string TableName = "Facts";

    public const string ContentColumn = nameof(Content);

    public const string AccessCountColumn = nameof(AccessCount);

    public const string LastAccessedAtColumn = nameof(LastAccessedAt);

    public long Id { get; set; }

    public string Content { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>How many times this fact has been returned by a search.</summary>
    public int AccessCount { get; set; }

    /// <summary>When this fact was last returned by a search. Null if never accessed.</summary>
    public DateTimeOffset? LastAccessedAt { get; set; }

    /// <summary>
    /// Native pgvector embedding for PostgreSQL ANN search.
    /// Only mapped by <see cref="Postgres.PostgresMemoryContext"/>; ignored by SQLite and MsSql contexts
    /// (SQLite uses a separate vec0 virtual table, MsSql uses in-process cosine).
    /// </summary>
    public Vector? Embedding { get; init; }

    public sealed class Configuration : IEntityTypeConfiguration<Fact>
    {
        public void Configure(EntityTypeBuilder<Fact> builder)
        {
            builder.ToTable(TableName);
            builder.HasKey(f => f.Id);
            builder.Property(f => f.Content).IsRequired();
            builder.Property(f => f.AccessCount).HasDefaultValue(0);
            builder.Property(f => f.LastAccessedAt).IsRequired(false);
            builder.HasIndex(f => f.CreatedAt);
            // Embedding is configured per-context: Postgres maps to vector(N), others Ignore it.
        }
    }
}