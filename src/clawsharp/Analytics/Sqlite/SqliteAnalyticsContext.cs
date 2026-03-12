using System.Diagnostics.CodeAnalysis;
using Clawsharp.Analytics.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Analytics.Sqlite;

public sealed class SqliteAnalyticsContext : DbContext
{
    public DbSet<InteractionEntity> Interactions { get; set; } = null!;

    [RequiresUnreferencedCode("EF Core uses reflection for model building. All entity types are statically referenced.")]
    [RequiresDynamicCode("EF Core requires dynamic code for query compilation. Use AOT-precompiled queries when possible.")]
    public SqliteAnalyticsContext(DbContextOptions<SqliteAnalyticsContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InteractionEntity.Configuration());

        modelBuilder.Entity<InteractionEntity>()
                    .Property(e => e.Timestamp)
                    .HasDefaultValueSql("datetime('now')");
    }
}
