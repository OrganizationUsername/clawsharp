using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawsharp.Memory.Entities;

/// <summary>
/// A conversation history summary entry.
/// WORM (Write-Once Read-Many): once written, history entries must not be modified or deleted.
/// They represent immutable compaction snapshots of conversation history.
/// </summary>
public sealed class HistoryEntry
{
    // Schema name constant — single source of truth for EF Core configuration and raw SQL.
    // Table name matches the DbSet property name (History).
    public const string TableName = "History";

    public long Id { get; set; } // EF auto-sets PK — keep set

    public string Summary { get; init; } = "";

    public DateTimeOffset Ts { get; init; }

    /// <summary>Private parameterless constructor for EF Core materialization.</summary>
    private HistoryEntry()
    {
    }

    public HistoryEntry(string summary, DateTimeOffset ts)
    {
        Summary = summary;
        Ts = ts;
    }

    public sealed class Configuration : IEntityTypeConfiguration<HistoryEntry>
    {
        public void Configure(EntityTypeBuilder<HistoryEntry> builder)
        {
            builder.ToTable(TableName);
            builder.HasKey(h => h.Id);
            builder.Property(h => h.Summary).IsRequired();
        }
    }
}