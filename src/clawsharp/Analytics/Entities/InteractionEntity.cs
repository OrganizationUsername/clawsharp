using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawsharp.Analytics.Entities;

/// <summary>
/// EF Core entity for persisted interaction records.
/// Tool calls are stored as a JSON TEXT column.
/// </summary>
public sealed class InteractionEntity
{
    public const string TableName = "Interactions";

    public long Id { get; set; }

    /// <summary>External GUID identifier (hex, 32 chars).</summary>
    public string ExternalId { get; init; } = "";

    public string SessionId { get; init; } = "";

    public string Channel { get; init; } = "";

    public string Model { get; init; } = "";

    public string UserPrompt { get; init; } = "";

    public string? Thinking { get; init; }

    public string Response { get; init; } = "";

    /// <summary>JSON-serialized <see cref="ToolCallSummary"/> list, or null.</summary>
    public string? ToolCallsJson { get; init; }

    public int ToolIterations { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CacheReadTokens { get; init; }

    public long CacheWriteTokens { get; init; }

    public double CostUsd { get; init; }

    public double CacheSavingsUsd { get; init; }

    public long DurationMs { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public sealed class Configuration : IEntityTypeConfiguration<InteractionEntity>
    {
        public void Configure(EntityTypeBuilder<InteractionEntity> builder)
        {
            builder.ToTable(TableName);
            builder.HasKey(e => e.Id);
            builder.Property(e => e.ExternalId).IsRequired().HasMaxLength(32);
            builder.Property(e => e.SessionId).IsRequired().HasMaxLength(256);
            builder.Property(e => e.Channel).IsRequired().HasMaxLength(64);
            builder.Property(e => e.Model).IsRequired().HasMaxLength(256);
            builder.Property(e => e.UserPrompt).IsRequired();
            builder.Property(e => e.Response).IsRequired();
            builder.HasIndex(e => e.Timestamp);
            builder.HasIndex(e => e.SessionId);
            builder.HasIndex(e => e.Model);
            builder.HasIndex(e => e.Channel);
        }
    }
}
