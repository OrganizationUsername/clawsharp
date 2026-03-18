using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawsharp.Analytics.Entities;

/// <summary>
/// Per-message row within an interaction: sent (user prompt), received (LLM response),
/// or thinking (LLM reasoning). 2-3 rows are created per interaction.
/// </summary>
public sealed class InteractionMessageEntity
{
    public const string TableName = "InteractionMessages";

    public long Id { get; set; }

    public long InteractionId { get; set; }

    /// <summary>Message discriminator: sent, received, or thinking.</summary>
    public MessageType MessageType { get; init; } = MessageType.Sent;

    public string Content { get; init; } = "";

    public int SequenceNumber { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public sealed class Configuration : IEntityTypeConfiguration<InteractionMessageEntity>
    {
        public void Configure(EntityTypeBuilder<InteractionMessageEntity> builder)
        {
            builder.ToTable(TableName);
            builder.HasKey(m => m.Id);
            builder.Property(m => m.MessageType)
                .HasConversion<MessageType.EfCoreValueConverter>()
                .IsRequired()
                .HasMaxLength(16);
            
            builder.Property(m => m.Content).IsRequired();
            builder.HasIndex(m => m.InteractionId);
            builder.HasIndex(m => m.MessageType);

            builder.HasOne<InteractionEntity>()
                .WithMany()
                .HasForeignKey(m => m.InteractionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
