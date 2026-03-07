using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Memory;

/// <summary>
/// Base DbContext for all clawsharp memory backends.
/// Provides WORM (Write-Once Read-Many) enforcement for HistoryEntry:
/// synchronous SaveChanges is always blocked; SaveChangesAsync validates
/// that HistoryEntry rows are never modified or deleted.
/// <para>
/// Note: Raw SQL (ExecuteSqlRawAsync, ExecuteDeleteAsync) bypasses these
/// EF-level checks. Database-level triggers enforce WORM at the DB layer.
/// </para>
/// </summary>
public abstract class MemoryDbContextBase : DbContext
{
    protected MemoryDbContextBase(DbContextOptions options) : base(options)
    {
    }

    public override int SaveChanges()
        => throw new InvalidOperationException(
            "Synchronous SaveChanges is not allowed. Use SaveChangesAsync.");

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new InvalidOperationException(
            "Synchronous SaveChanges is not allowed. Use SaveChangesAsync.");

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateWormSemantics();
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateWormSemantics();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Enforces WORM (Write-Once Read-Many) semantics on HistoryEntry.
    /// Conversation history entries are immutable once written — they represent
    /// a compaction snapshot and must not be modified or deleted.
    /// </summary>
    private void ValidateWormSemantics()
    {
        var hasIllegalHistoryMutation = ChangeTracker.Entries<HistoryEntry>()
                                                     .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (hasIllegalHistoryMutation)
        {
            throw new InvalidOperationException(
                "HistoryEntry is append-only (WORM — Write Once, Read Many). " +
                "UPDATE and DELETE operations are not allowed. " +
                "HistoryEntry records are immutable compaction snapshots of conversation history.");
        }
    }
}