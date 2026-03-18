using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawsharp.Analytics.Entities;

/// <summary>
/// Groups all interactions belonging to the same session into a single conversation thread.
/// </summary>
public sealed class ConversationThread
{
    public const string TableName = "ConversationThreads";

    public long Id { get; set; }

    public string SessionId { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public sealed class Configuration : IEntityTypeConfiguration<ConversationThread>
    {
        public void Configure(EntityTypeBuilder<ConversationThread> builder)
        {
            builder.ToTable(TableName);
            builder.HasKey(t => t.Id);
            builder.Property(t => t.SessionId).IsRequired().HasMaxLength(256);
            builder.HasIndex(t => t.SessionId).IsUnique();
        }
    }
}
