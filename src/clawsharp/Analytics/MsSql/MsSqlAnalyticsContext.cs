using System.Diagnostics.CodeAnalysis;
using Clawsharp.Analytics.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Analytics.MsSql;

[method: RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
[method: RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
public sealed class MsSqlAnalyticsContext(DbContextOptions<MsSqlAnalyticsContext> options) : DbContext(options)
{
    public DbSet<InteractionEntity> Interactions { get; set; } = null!;
    public DbSet<ConversationThread> ConversationThreads { get; set; } = null!;
    public DbSet<InteractionMessageEntity> InteractionMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InteractionEntity.Configuration());
        modelBuilder.ApplyConfiguration(new ConversationThread.Configuration());
        modelBuilder.ApplyConfiguration(new InteractionMessageEntity.Configuration());

        modelBuilder.Entity<InteractionEntity>()
                    .Property(e => e.Timestamp)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        modelBuilder.Entity<ConversationThread>()
                    .Property(t => t.CreatedAt)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()");

        modelBuilder.Entity<InteractionMessageEntity>()
                    .Property(m => m.Timestamp)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()");
    }
}
