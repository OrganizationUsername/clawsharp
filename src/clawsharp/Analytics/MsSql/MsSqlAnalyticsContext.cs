using System.Diagnostics.CodeAnalysis;
using Clawsharp.Analytics.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Analytics.MsSql;

public sealed class MsSqlAnalyticsContext : DbContext
{
    public DbSet<InteractionEntity> Interactions { get; set; } = null!;

    [RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
    [RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
    public MsSqlAnalyticsContext(DbContextOptions<MsSqlAnalyticsContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InteractionEntity.Configuration());

        modelBuilder.Entity<InteractionEntity>()
                    .Property(e => e.Timestamp)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()");
    }
}
