using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Memory.MsSql;

/// <summary>
///     Microsoft SQL Server-backed memory store using Entity Framework Core.
///     Search uses CONTAINS (if full-text index configured) with LIKE fallback.
/// </summary>
public sealed partial class MsSqlMemory : IMemory
{
    private const int RecentContentLimit = 50;

    private readonly IDbContextFactory<MsSqlMemoryContext> _contextFactory;

    private volatile Task? _initTask;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    private readonly ILogger<MsSqlMemory> _logger;

    // Compiled queries — pre-compiled LINQ expression trees for frequently-called read paths.
    // Thread-safe: the compiled delegate is a pure function; context instance is passed at call time.

    private static readonly Func<MsSqlMemoryContext, IAsyncEnumerable<string>>
        GetRecentContentQuery = EF.CompileAsyncQuery((MsSqlMemoryContext db) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Take(RecentContentLimit)
              .Select(f => f.Content));

    private static readonly Func<MsSqlMemoryContext, string, int, IAsyncEnumerable<string>>
        SearchLikeFallbackQuery = EF.CompileAsyncQuery((MsSqlMemoryContext db, string pattern, int limit) =>
            db.Facts
              .AsNoTracking()
              .Where(f => EF.Functions.Like(f.Content, pattern))
              .OrderByDescending(f => f.Id)
              .Take(limit)
              .Select(f => f.Content));

    private static readonly Func<MsSqlMemoryContext, string, int, IAsyncEnumerable<Fact>>
        SearchHybridLikeQuery = EF.CompileAsyncQuery((MsSqlMemoryContext db, string pattern, int topK) =>
            db.Facts
              .AsNoTracking()
              .Where(f => EF.Functions.Like(f.Content, pattern))
              .OrderByDescending(f => f.Id)
              .Take(topK));

    private static readonly Func<MsSqlMemoryContext, IAsyncEnumerable<Fact>>
        ListAllFactsQuery = EF.CompileAsyncQuery((MsSqlMemoryContext db) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Select(f => f));

    public MsSqlMemory(IDbContextFactory<MsSqlMemoryContext> contextFactory, ILogger<MsSqlMemory> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<string?> GetContextAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var facts = new List<string>();
        await foreach (var content in GetRecentContentQuery(context).WithCancellation(ct))
        {
            facts.Add($"- {content}");
        }

        return facts.Count > 0 ? "## Memory\n" + string.Join("\n", facts) : null;
    }

    public async Task AppendFactAsync(string fact, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.Facts.Add(new Fact { Content = fact, CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync(ct);
    }

    public async Task AppendHistoryAsync(string summary, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.History.Add(new HistoryEntry(summary, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int n = 5, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        List<string> results = [];
        try
        {
            // Sanitize: strip double-quote chars from words to prevent CONTAINS syntax injection
            var containsQuery = string.Join(" OR ",
                query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Select(w => $"\"{w.Replace("\"", "")}\""));

            results = await context.Facts
                                   .AsNoTracking()
                                   .Where(f => EF.Functions.Contains(f.Content, containsQuery))
                                   .OrderByDescending(f => f.Id)
                                   .Take(n)
                                   .Select(f => f.Content)
                                   .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            // FTS not configured -- fall through to LIKE
            LogMemoryOperationFailed(_logger, ex, ex.Message);
        }

        if (results.Count == 0)
        {
            var pattern = $"%{EscapeLikePattern(query)}%";
            await foreach (var content in SearchLikeFallbackQuery(context, pattern, n).WithCancellation(ct))
            {
                results.Add(content);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<Fact>> SearchHybridAsync(string query, float[]? queryEmbedding = null, int topK = 5,
                                                             CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        // MsSql backend does not support embeddings — fall back to LIKE search
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var pattern = $"%{EscapeLikePattern(query)}%";
        var results = new List<Fact>();
        await foreach (var fact in SearchHybridLikeQuery(context, pattern, topK).WithCancellation(ct))
        {
            results.Add(fact);
        }

        var ids = results.Select(f => f.Id).ToList();
        if (ids.Count > 0)
        {
            await UpdateAccessCountsAsync(ids, ct);
        }

        return results;
    }

    public async Task<IReadOnlyList<Fact>> ListFactsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var facts = new List<Fact>();
        await foreach (var fact in ListAllFactsQuery(context).WithCancellation(ct))
        {
            facts.Add(fact);
        }

        return facts;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        // NOTE: Raw SQL bypasses EF WORM validation; database triggers enforce the constraint at DB level.
        await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {Fact.TableName}", ct);
        // History entries are WORM (write-once read-many) — never deleted.
        // They represent immutable compaction snapshots and are preserved across clears.
    }

    public async Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTimeOffset.UtcNow - maxAge;

        // NOTE: Raw SQL bypasses EF WORM validation; database triggers enforce the constraint at DB level.
        // Use raw SQL for cross-provider consistency and to avoid LINQ translation issues
        var deleted = await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {Fact.TableName} WHERE [CreatedAt] < {{0}}", new object[] { cutoff }, ct);

        return deleted;
    }

    private async Task UpdateAccessCountsAsync(List<long> ids, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var now = DateTimeOffset.UtcNow;
            // NOTE: Raw SQL bypasses EF WORM validation; database triggers enforce the constraint at DB level.
            // Batch update — IDs are long values so string joining is safe (no injection risk)
            var idList = string.Join(",", ids);
            await ctx.Database.ExecuteSqlRawAsync(
                $"UPDATE {Fact.TableName} SET {Fact.AccessCountColumn} = {Fact.AccessCountColumn} + 1, {Fact.LastAccessedAtColumn} = {{0}} WHERE [Id] IN ({idList})",
                new object[] { now }, ct);
        }
        catch (Exception ex)
        {
            // Non-fatal — access tracking failure does not affect search results
            LogMemoryOperationFailed(_logger, ex, ex.Message);
        }
    }

    /// <summary>Escape LIKE metacharacters for SQL Server (bracket escape convention).</summary>
    private static string EscapeLikePattern(string query) =>
        query.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initTask is { IsCompletedSuccessfully: true })
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initTask is { IsCompletedSuccessfully: true })
            {
                return;
            }

            var task = InitSchemaAsync(ct);
            _initTask = task;
            await task;
        }
        catch
        {
            _initTask = null; // allow retry on next call
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    [RequiresDynamicCode(
        "EF Core MigrateAsync builds the design-time model at runtime. Not compatible with NativeAOT; use migration bundles for AOT deployment.")]
    private async Task InitSchemaAsync(CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        // MigrateAsync applies pending migrations. For existing databases originally created via
        // EnsureCreated, the __EFMigrationsHistory table will be created and the initial migration
        // marked as applied if the schema already matches.
        using var migrationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        migrationCts.CancelAfter(TimeSpan.FromSeconds(30));
        await context.Database.MigrateAsync(migrationCts.Token);
        // Add access tracking columns if not already present
        await context.Database.ExecuteSqlRawAsync($"""
                                                   IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('{Fact.TableName}') AND name = '{Fact.AccessCountColumn}')
                                                   BEGIN
                                                       ALTER TABLE {Fact.TableName} ADD {Fact.AccessCountColumn} INT NOT NULL DEFAULT 0;
                                                       ALTER TABLE {Fact.TableName} ADD {Fact.LastAccessedAtColumn} DATETIMEOFFSET NULL;
                                                   END
                                                   """);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Memory operation failed: {Message}")]
    private static partial void LogMemoryOperationFailed(ILogger logger, Exception exception, string message);
}