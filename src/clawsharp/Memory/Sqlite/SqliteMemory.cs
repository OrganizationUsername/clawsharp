using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Memory;

namespace Clawsharp.Memory.Sqlite;

/// <summary>
///     SQLite-backed memory store using Entity Framework Core.
///     Full-text search uses SQLite FTS5 virtual tables.
///     Optionally stores embeddings for hybrid semantic + keyword search.
///     When sqlite-vec is available, uses vec0 virtual table for O(log N) ANN search;
///     otherwise falls back to FTS5 pre-filter + in-process cosine scoring.
/// </summary>
public sealed partial class SqliteMemory : IMemory
{
    private const int CandidateLimit = 500;

    private const int RecentContentLimit = 50;

    private const int OversampleFactor = 3;

    private const float VectorWeight = 0.6f;

    private const float KeywordWeight = 0.4f;

    // SQLite virtual table names — derived from the base table so they stay in sync.
    private const string FtsTable = Fact.TableName + "_fts";

    private const string VecTable = Fact.TableName + "_vec";

    private const string EmbeddingJsonColumn = "embedding";

    private readonly IDbContextFactory<SqliteMemoryContext> _contextFactory;

    private readonly IEmbeddingProvider? _embeddingProvider;

    private readonly double _halfLifeDays;

    private readonly int _embeddingDimension;

    private volatile Task? _initTask;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    private readonly ILogger<SqliteMemory> _logger;

    /// <summary>Whether the vec0 virtual table was successfully created during init.</summary>
    private bool _vecTableReady;

    // Compiled queries — pre-compiled LINQ expression trees for frequently-called read paths.
    // Thread-safe: the compiled delegate is a pure function; context instance is passed at call time.

    private static readonly Func<SqliteMemoryContext, IAsyncEnumerable<string>>
        GetRecentContentQuery = EF.CompileAsyncQuery((SqliteMemoryContext db) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Take(RecentContentLimit)
              .Select(f => f.Content));

    private static readonly Func<SqliteMemoryContext, string, int, IAsyncEnumerable<string>>
        SearchLikeFallbackQuery = EF.CompileAsyncQuery((SqliteMemoryContext db, string pattern, int limit) =>
            db.Facts
              .AsNoTracking()
              .Where(f => EF.Functions.Like(f.Content, pattern, @"\"))
              .OrderByDescending(f => f.Id)
              .Take(limit)
              .Select(f => f.Content));

    private static readonly Func<SqliteMemoryContext, string, int, IAsyncEnumerable<Fact>>
        SearchHybridLikeQuery = EF.CompileAsyncQuery((SqliteMemoryContext db, string pattern, int topK) =>
            db.Facts
              .AsNoTracking()
              .Where(f => EF.Functions.Like(f.Content, pattern, @"\"))
              .OrderByDescending(f => f.Id)
              .Take(topK));

    private static readonly Func<SqliteMemoryContext, IAsyncEnumerable<Fact>>
        ListAllFactsQuery = EF.CompileAsyncQuery((SqliteMemoryContext db) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Select(f => f));

    public SqliteMemory(
        IDbContextFactory<SqliteMemoryContext> contextFactory,
        ILogger<SqliteMemory> logger,
        IEmbeddingProvider? embeddingProvider = null,
        IOptions<MemoryConfig>? memoryConfig = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _embeddingProvider = embeddingProvider;
        _halfLifeDays = memoryConfig?.Value.Decay?.HalfLifeDays ?? 0;
        _embeddingDimension = memoryConfig?.Value.EmbeddingDimension ?? 1536;
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

        // Wrap fact + FTS insert in a transaction to prevent orphaned data on crash
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var entity = new Fact { Content = fact, CreatedAt = DateTimeOffset.UtcNow };
            context.Facts.Add(entity);
            await context.SaveChangesAsync(ct);

            // Keep the FTS5 shadow table in sync
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO {FtsTable}(rowid, {Fact.ContentColumn}) VALUES ({{0}}, {{1}})",
                new object[] { entity.Id, fact }, ct);

            await transaction.CommitAsync(ct);

            // Embed and store vector if provider is configured (outside transaction — non-critical)
            if (_embeddingProvider is not null)
            {
                try
                {
                    var embedding = await _embeddingProvider.EmbedAsync(fact, ct);
                    var json = EmbeddingMath.Serialize(embedding);

                    // Store in TEXT column (legacy, used by fallback path)
                    await context.Database.ExecuteSqlRawAsync(
                        $"UPDATE {Fact.TableName} SET {EmbeddingJsonColumn} = {{0}} WHERE \"Id\" = {{1}}",
                        new object[] { json, entity.Id }, ct);

                    // Also insert into vec0 virtual table if available
                    if (_vecTableReady && SqliteVecConnectionInterceptor.VecExtensionLoaded)
                    {
                        try
                        {
                            await context.Database.ExecuteSqlRawAsync(
                                $"INSERT INTO {VecTable}(rowid, {EmbeddingJsonColumn}) VALUES ({{0}}, {{1}})",
                                new object[] { entity.Id, json }, ct);
                        }
                        catch (Exception ex)
                        {
                            LogVecInsertFailed(_logger, ex, entity.Id, ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Embedding failure is non-fatal — fact is still stored without a vector
                    LogMemoryOperationFailed(_logger, ex, ex.Message);
                }
            }
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
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
        try
        {
            return await context.Database
                                .SqlQueryRaw<string>($$"""
                                                       SELECT f.{{Fact.ContentColumn}} AS "Value" FROM {{FtsTable}}
                                                       JOIN {{Fact.TableName}} f ON {{FtsTable}}.rowid = f."Id"
                                                       WHERE {{FtsTable}} MATCH {0}
                                                       ORDER BY rank
                                                       LIMIT {1}
                                                       """, SanitizeFtsQuery(query), n)
                                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            // FTS5 match error (e.g. syntax) -- fall back to LIKE
            LogMemoryOperationFailed(_logger, ex, ex.Message);
            var pattern = $"%{EscapeLikePattern(query)}%";
            var results = new List<string>();
            await foreach (var content in SearchLikeFallbackQuery(context, pattern, n).WithCancellation(ct))
            {
                results.Add(content);
            }

            return results;
        }
    }

    public async Task<IReadOnlyList<Fact>> SearchHybridAsync(string query, float[]? queryEmbedding = null, int topK = 5,
                                                             CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // If no query embedding, fall back to LIKE search returning Fact objects
        if (queryEmbedding is null || queryEmbedding.Length == 0)
        {
            var pattern = $"%{EscapeLikePattern(query)}%";
            var candidates = new List<Fact>();
            await foreach (var fact in SearchHybridLikeQuery(context, pattern, topK).WithCancellation(ct))
            {
                candidates.Add(fact);
            }

            return candidates;
        }

        // Try sqlite-vec ANN search if extension is loaded and table is ready
        if (_vecTableReady && SqliteVecConnectionInterceptor.VecExtensionLoaded)
        {
            try
            {
                return await SearchHybridVecAsync(query, queryEmbedding, topK, context, ct);
            }
            catch (Exception ex)
            {
                LogVecAnnSearchFailed(_logger, ex, ex.Message);
            }
        }

        // Fallback: FTS5 pre-filter + in-process cosine scoring
        return await SearchHybridFallbackAsync(query, queryEmbedding, topK, context, ct);
    }

    /// <summary>Internal DTO for vec0 KNN query results.</summary>
    private sealed class VecResult
    {
        public long Id { get; set; }

        public double Distance { get; set; }
    }

    /// <summary>
    /// sqlite-vec ANN search: vec0 KNN query + keyword score merge for hybrid ranking.
    /// </summary>
    private async Task<IReadOnlyList<Fact>> SearchHybridVecAsync(
        string query, float[] queryEmbedding, int topK, SqliteMemoryContext context, CancellationToken ct)
    {
        var queryJson = EmbeddingMath.Serialize(queryEmbedding);

        var vecResults = await context.Database
                                      .SqlQueryRaw<VecResult>($$"""
                                                                SELECT v.rowid AS "Id", v.distance AS "Distance"
                                                                FROM {{VecTable}} v
                                                                WHERE v.{{EmbeddingJsonColumn}} MATCH {0}
                                                                  AND k = {1}
                                                                ORDER BY v.distance
                                                                """, queryJson, topK * OversampleFactor)
                                      .ToListAsync(ct);

        if (vecResults.Count == 0)
        {
            return [];
        }

        var ids = vecResults.Select(r => r.Id).ToList();
        var facts = await context.Facts
                                 .AsNoTracking()
                                 .Where(f => ids.Contains(f.Id))
                                 .ToListAsync(ct);

        // Merge ANN distance with keyword score for hybrid re-rank
        var distanceMap = vecResults.ToDictionary(r => r.Id, r => r.Distance);

        var scored = facts.Select(f =>
                          {
                              // Convert cosine distance (0=identical, 2=opposite) to similarity (1=identical, -1=opposite)
                              float vecScore = 1f - (float)(distanceMap.GetValueOrDefault(f.Id, 2.0) / 2.0);
                              float keywordScore = f.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
                              float hybrid = vecScore * VectorWeight + keywordScore * KeywordWeight;

                              if (_halfLifeDays > 0)
                              {
                                  hybrid = MemoryDecayScoring.ApplyDecayWithUsage(
                                      hybrid, f.CreatedAt, _halfLifeDays, f.AccessCount, f.LastAccessedAt);
                              }

                              return (f, hybrid);
                          })
                          .OrderByDescending(x => x.hybrid)
                          .Take(topK)
                          .Select(x => x.f)
                          .ToList();

        var returnedIds = scored.Select(f => f.Id).ToList();
        if (returnedIds.Count > 0)
        {
            await UpdateAccessCountsAsync(returnedIds, ct);
        }

        return scored;
    }

    /// <summary>
    /// Legacy fallback: FTS5 pre-filter + in-process cosine scoring.
    /// Used when sqlite-vec is not available.
    /// </summary>
    private async Task<IReadOnlyList<Fact>> SearchHybridFallbackAsync(
        string query, float[] queryEmbedding, int topK, SqliteMemoryContext context, CancellationToken ct)
    {
        List<FactWithEmbedding> rows;
        try
        {
            rows = await context.Database
                                .SqlQueryRaw<FactWithEmbedding>($$"""
                                                                  SELECT f."Id", f."Content", f."CreatedAt",
                                                                         f.{{Fact.AccessCountColumn}} AS "AccessCount", f.{{Fact.LastAccessedAtColumn}} AS "LastAccessedAt",
                                                                         f.{{EmbeddingJsonColumn}} AS "EmbeddingJson"
                                                                  FROM {{FtsTable}}
                                                                  JOIN {{Fact.TableName}} f ON {{FtsTable}}.rowid = f."Id"
                                                                  WHERE {{FtsTable}} MATCH {0}
                                                                  ORDER BY rank
                                                                  LIMIT {{CandidateLimit}}
                                                                  """, SanitizeFtsQuery(query))
                                .ToListAsync(ct);

            // FTS5 may return 0 results on no match — fall back to most-recent facts
            if (rows.Count == 0)
            {
                rows = await context.Database
                                    .SqlQuery<FactWithEmbedding>($"""
                                                                  SELECT "Id", "Content", "CreatedAt",
                                                                         {Fact.AccessCountColumn} AS "AccessCount", {Fact.LastAccessedAtColumn} AS "LastAccessedAt",
                                                                         {EmbeddingJsonColumn} AS "EmbeddingJson"
                                                                  FROM {Fact.TableName} ORDER BY "Id" DESC LIMIT {CandidateLimit}
                                                                  """)
                                    .ToListAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // FTS5 syntax error — fall back to most-recent N facts
            LogMemoryOperationFailed(_logger, ex, ex.Message);
            rows = await context.Database
                                .SqlQueryRaw<FactWithEmbedding>($"""
                                                                 SELECT "Id", "Content", "CreatedAt",
                                                                        {Fact.AccessCountColumn} AS "AccessCount", {Fact.LastAccessedAtColumn} AS "LastAccessedAt",
                                                                        {EmbeddingJsonColumn} AS "EmbeddingJson"
                                                                 FROM {Fact.TableName} ORDER BY "Id" DESC LIMIT {CandidateLimit}
                                                                 """)
                                .ToListAsync(ct);
        }

        var scored = rows.Select(row =>
                         {
                             // Vector similarity score (0..1)
                             float vectorScore = 0f;
                             if (row.EmbeddingJson is not null)
                             {
                                 var vec = EmbeddingMath.Deserialize(row.EmbeddingJson);
                                 if (vec.Length > 0)
                                 {
                                     vectorScore = EmbeddingMath.CosineSimilarity(queryEmbedding, vec);
                                 }
                             }

                             float keywordScore = row.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;

                             // Hybrid score: 0.6 vector + 0.4 keyword (vector-weighted since we have embeddings)
                             float hybrid = vectorScore * VectorWeight + keywordScore * KeywordWeight;

                             // Apply time-based decay with usage boost if configured
                             if (_halfLifeDays > 0)
                             {
                                 hybrid = MemoryDecayScoring.ApplyDecayWithUsage(
                                     hybrid, row.CreatedAt, _halfLifeDays, row.AccessCount, row.LastAccessedAt);
                             }

                             return (row, hybrid);
                         })
                         .OrderByDescending(x => x.hybrid)
                         .Take(topK)
                         .Select(x => new Fact
                         {
                             Id = x.row.Id, Content = x.row.Content, CreatedAt = x.row.CreatedAt,
                             AccessCount = x.row.AccessCount, LastAccessedAt = x.row.LastAccessedAt
                         })
                         .ToList();

        var returnedIds = scored.Select(f => f.Id).ToList();
        if (returnedIds.Count > 0)
        {
            await UpdateAccessCountsAsync(returnedIds, ct);
        }

        return scored;
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
        await context.Database.ExecuteSqlRawAsync($"DELETE FROM {FtsTable}", ct);

        // Clear vec0 table if available
        if (_vecTableReady && SqliteVecConnectionInterceptor.VecExtensionLoaded)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync($"DELETE FROM {VecTable}", ct);
            }
            catch (Exception ex)
            {
                LogVecClearFailed(_logger, ex, ex.Message);
            }
        }

        await context.Database.ExecuteSqlRawAsync($"DELETE FROM {Fact.TableName}", ct);
        // History entries are WORM (write-once read-many) — never deleted.
        // They represent immutable compaction snapshots and are preserved across clears.
    }

    public async Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var cutoff = (DateTimeOffset.UtcNow - maxAge).ToString("O");

        // NOTE: Raw SQL bypasses EF WORM validation; database triggers enforce the constraint at DB level.
        // SQLite cannot translate DateTimeOffset comparisons in LINQ — use raw SQL.
        // SQLite stores DateTimeOffset as ISO 8601 text; string comparison works for ordering.
        var expired = await context.Database
                                   .SqlQueryRaw<long>($$"""
                                                        SELECT "Id" AS "Value" FROM {{Fact.TableName}} WHERE "CreatedAt" < {0}
                                                        """, cutoff)
                                   .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return 0;
        }

        // Batch delete from FTS5 shadow table, vec0 table, and facts atomically to prevent index desync on crash.
        // IDs are long values so string joining is safe (no injection risk) — same pattern as UpdateAccessCountsAsync.
        var idList = string.Join(",", expired);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {FtsTable} WHERE rowid IN ({idList})", ct);

            if (_vecTableReady && SqliteVecConnectionInterceptor.VecExtensionLoaded)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM {VecTable} WHERE rowid IN ({idList})", ct);
                }
                catch (Exception ex)
                {
                    LogVecBatchDeleteFailed(_logger, ex, ex.Message);
                }
            }

            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{Fact.TableName}\" WHERE \"Id\" IN ({idList})", ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return expired.Count;
    }

    private async Task UpdateAccessCountsAsync(List<long> ids, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var now = DateTimeOffset.UtcNow.ToString("O");
            // NOTE: Raw SQL bypasses EF WORM validation; database triggers enforce the constraint at DB level.
            // Batch update — IDs are long values so string joining is safe (no injection risk)
            var idList = string.Join(",", ids);
            await ctx.Database.ExecuteSqlRawAsync(
                $"UPDATE {Fact.TableName} SET {Fact.AccessCountColumn} = {Fact.AccessCountColumn} + 1, {Fact.LastAccessedAtColumn} = {{0}} WHERE \"Id\" IN ({idList})",
                new object[] { now }, ct);
        }
        catch (Exception ex)
        {
            // Non-fatal — access tracking failure does not affect search results
            LogMemoryOperationFailed(_logger, ex, ex.Message);
        }
    }

    /// <summary>Escape LIKE metacharacters for SQLite (backslash escape convention).</summary>
    private static string EscapeLikePattern(string query) =>
        query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static string SanitizeFtsQuery(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", words.Select(w => $"\"{w.Replace("\"", "\"\"")}\""));
    }

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
        // FTS5 virtual table (content-table backed by facts)
        await context.Database.ExecuteSqlRawAsync($"""
                                                   CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTable}
                                                   USING fts5({Fact.ContentColumn}, content={Fact.TableName}, content_rowid=id);
                                                   """);
        // Add embedding column if not present (graceful migration)
        try
        {
            await context.Database.ExecuteSqlRawAsync($"ALTER TABLE {Fact.TableName} ADD COLUMN {EmbeddingJsonColumn} TEXT");
        }
        catch
        {
            // Column already exists — ignore
        }

        // Add access tracking columns if not present (graceful migration)
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {Fact.TableName} ADD COLUMN {Fact.AccessCountColumn} INTEGER NOT NULL DEFAULT 0");
        }
        catch
        {
            // Column already exists — ignore
        }

        try
        {
            await context.Database.ExecuteSqlRawAsync($"ALTER TABLE {Fact.TableName} ADD COLUMN {Fact.LastAccessedAtColumn} TEXT");
        }
        catch
        {
            // Column already exists — ignore
        }

        // sqlite-vec: create vec0 virtual table for ANN search if extension is loaded
        if (SqliteVecConnectionInterceptor.VecExtensionLoaded)
        {
            try
            {
                var dim = _embeddingDimension;
                // DDL: column type definition cannot be parameterized; dim is a trusted int from config
                var ddl = $"CREATE VIRTUAL TABLE IF NOT EXISTS {VecTable} USING vec0({EmbeddingJsonColumn} float[{dim}])";
                await context.Database.ExecuteSqlRawAsync(ddl);
                _vecTableReady = true;
                LogSqliteVecLoaded(_logger, dim);
            }
            catch (Exception ex)
            {
                _vecTableReady = false;
                LogVecTableCreateFailed(_logger, ex, ex.Message);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Memory operation failed: {Message}")]
    private static partial void LogMemoryOperationFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to insert into facts_vec for fact {Id}: {Message}")]
    private static partial void LogVecInsertFailed(ILogger logger, Exception exception, long id, string message);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "sqlite-vec ANN search failed, falling back to FTS5 + in-process cosine: {Message}")]
    private static partial void LogVecAnnSearchFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to clear facts_vec: {Message}")]
    private static partial void LogVecClearFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Failed to batch-delete from facts_vec: {Message}")]
    private static partial void LogVecBatchDeleteFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "sqlite-vec loaded; vec0 ANN index active with float[{Dim}]")]
    private static partial void LogSqliteVecLoaded(ILogger logger, int dim);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Failed to create facts_vec virtual table; ANN search disabled: {Message}")]
    private static partial void LogVecTableCreateFailed(ILogger logger, Exception exception, string message);
}