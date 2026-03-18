using System.Diagnostics.CodeAnalysis;
using Clawsharp.Memory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Clawsharp.Config.Memory;

namespace Clawsharp.Memory.Postgres;

/// <summary>
///     PostgreSQL-backed memory store using Entity Framework Core.
///     Full-text search uses PostgreSQL's built-in tsvector / websearch_to_tsquery
///     with the 'simple' dictionary (locale-agnostic, matches InvariantGlobalization=true).
///     Vector search uses pgvector HNSW index for native ANN cosine distance queries,
///     eliminating the need to load 500 candidates into process memory for scoring.
/// </summary>
public sealed partial class PostgresMemory(
    IDbContextFactory<PostgresMemoryContext> contextFactory,
    ILogger<PostgresMemory> logger,
    IEmbeddingProvider? embeddingProvider = null,
    IOptions<MemoryConfig>? memoryConfig = null)
    : IMemory
{
    private const int CandidateLimit = 500;

    private const int RecentContentLimit = 50;

    private const int OversampleFactor = 3;

    private const float VectorWeight = 0.6f;

    private const float KeywordWeight = 0.4f;

    private readonly double _halfLifeDays = memoryConfig?.Value.Decay?.HalfLifeDays ?? 0;

    private readonly int _embeddingDimension = memoryConfig?.Value.EmbeddingDimension ?? 1536;

    private volatile Task? _initTask;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>Whether the pgvector extension is available. Set during schema init.</summary>
    private bool _pgvectorAvailable;

    private static readonly Func<PostgresMemoryContext, IAsyncEnumerable<string>>
        GetRecentContentQuery = EF.CompileAsyncQuery((PostgresMemoryContext db) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Take(RecentContentLimit)
              .Select(f => f.Content));

    private static readonly Func<PostgresMemoryContext, string, int, IAsyncEnumerable<string>>
        SearchILikeFallbackQuery = EF.CompileAsyncQuery((PostgresMemoryContext db, string pattern, int limit) =>
            db.Facts
              .AsNoTracking()
              .Where(f => EF.Functions.ILike(f.Content, pattern, @"\"))
              .OrderByDescending(f => f.Id)
              .Take(limit)
              .Select(f => f.Content));

    private static readonly Func<PostgresMemoryContext, string, int, IAsyncEnumerable<Fact>>
        SearchHybridILikeQuery = EF.CompileAsyncQuery((PostgresMemoryContext db, string pattern, int topK) =>
            db.Facts
              .AsNoTracking()
              .Where(f => EF.Functions.ILike(f.Content, pattern, @"\"))
              .OrderByDescending(f => f.Id)
              .Take(topK));

    private static readonly Func<PostgresMemoryContext, int, IAsyncEnumerable<Fact>>
        GetRecentFactsQuery = EF.CompileAsyncQuery((PostgresMemoryContext db, int topK) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Take(topK));

    private static readonly Func<PostgresMemoryContext, IAsyncEnumerable<Fact>>
        ListAllFactsQuery = EF.CompileAsyncQuery((PostgresMemoryContext db) =>
            db.Facts
              .AsNoTracking()
              .OrderByDescending(f => f.Id)
              .Select(f => f));

    public async Task<string?> GetContextAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await contextFactory.CreateDbContextAsync(ct);
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
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        float[]? embedding = null;
        if (embeddingProvider is not null)
        {
            try
            {
                embedding = await embeddingProvider.EmbedAsync(fact, ct);
            }
            catch (Exception ex)
            {
                LogMemoryOperationFailed(logger, ex, ex.Message);
            }
        }

        Vector? embeddingVector = null;
        if (embedding is not null && _pgvectorAvailable)
        {
            embeddingVector = new Vector(embedding);
        }

        var entity = new Fact
        {
            Content = fact,
            CreatedAt = DateTimeOffset.UtcNow,
            Embedding = embeddingVector
        };

        context.Facts.Add(entity);
        await context.SaveChangesAsync(ct);

        // Legacy fallback: write JSON TEXT column when pgvector is not available
        if (embedding is not null && !_pgvectorAvailable)
        {
            try
            {
                var json = EmbeddingMath.Serialize(embedding);
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE \"{Fact.TableName}\" SET embedding = {{0}} WHERE \"Id\" = {{1}}",
                    new object[] { json, entity.Id }, ct);
            }
            catch (Exception ex)
            {
                LogMemoryOperationFailed(logger, ex, ex.Message);
            }
        }
    }

    public async Task AppendHistoryAsync(string summary, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        context.History.Add(new HistoryEntry(summary, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int n = 5, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        const string sql =
            $$"""
            SELECT "{{Fact.ContentColumn}}" AS "Value" FROM "{{Fact.TableName}}"
            WHERE content_tsv @@ websearch_to_tsquery('simple', {0})
            ORDER BY ts_rank(content_tsv, websearch_to_tsquery('simple', {1})) DESC
            LIMIT {2}
            """;

        var results = await context.Database
                                   .SqlQueryRaw<string>(sql, query, query, n)
                                   .ToListAsync(ct);

        if (results.Count == 0)
        {
            var pattern = $"%{EscapeLikePattern(query)}%";
            await foreach (var content in SearchILikeFallbackQuery(context, pattern, n).WithCancellation(ct))
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
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // If no query embedding, fall back to ILIKE search returning Fact objects
        if (queryEmbedding is null || queryEmbedding.Length == 0)
        {
            var pattern = $"%{EscapeLikePattern(query)}%";
            var candidates = new List<Fact>();
            await foreach (var fact in SearchHybridILikeQuery(context, pattern, topK).WithCancellation(ct))
            {
                candidates.Add(fact);
            }

            var ids = candidates.Select(f => f.Id).ToList();
            if (ids.Count > 0)
            {
                await UpdateAccessCountsAsync(ids, ct);
            }

            return candidates;
        }

        // Use native pgvector ANN if available, otherwise fall back to in-process cosine
        if (_pgvectorAvailable)
        {
            return await SearchHybridPgvectorAsync(query, queryEmbedding, topK, context, ct);
        }

        return await SearchHybridFallbackAsync(query, queryEmbedding, topK, context, ct);
    }

    /// <summary>
    /// Native pgvector hybrid search: FTS pre-filter + ANN rerank, all database-side.
    /// </summary>
    private async Task<IReadOnlyList<Fact>> SearchHybridPgvectorAsync(
        string query, float[] queryEmbedding, int topK, PostgresMemoryContext context, CancellationToken ct)
    {
        var queryVector = new Vector(queryEmbedding);

        // Step 1: FTS pre-filter candidates
        List<long> candidateIds;
        try
        {
            var sql =
                $$"""
                SELECT "Id" AS "Value" FROM "{{Fact.TableName}}"
                WHERE content_tsv @@ websearch_to_tsquery('simple', {0})
                ORDER BY ts_rank(content_tsv, websearch_to_tsquery('simple', {1})) DESC
                LIMIT {{CandidateLimit}}
                """;

            candidateIds = await context.Database
                                        .SqlQueryRaw<long>(sql, query, query)
                                        .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            LogMemoryOperationFailed(logger, ex, ex.Message);
            candidateIds = [];
        }

        // Step 2: pgvector ANN rerank among candidates (or global if FTS returned nothing)
        IQueryable<Fact> annQuery = context.Facts.AsNoTracking().Where(f => f.Embedding != null);

        if (candidateIds.Count > 0)
        {
            annQuery = annQuery.Where(f => candidateIds.Contains(f.Id));
        }

        // Oversample for hybrid re-ranking with keyword score and decay.
        // Project both the Fact and the DB-computed cosine distance to avoid
        // recomputing similarity in-process (MED-65: eliminate double cosine computation).
        var candidatesWithDistance = await annQuery
                                           .OrderBy(f => f.Embedding!.CosineDistance(queryVector))
                                           .Take(topK * OversampleFactor)
                                           .Select(f => new { Fact = f, Distance = f.Embedding!.CosineDistance(queryVector) })
                                           .ToListAsync(ct);

        if (candidatesWithDistance.Count == 0)
        {
            // No embeddings at all — fall back to most recent facts
            var recentFacts = new List<Fact>();
            await foreach (var fact in GetRecentFactsQuery(context, topK).WithCancellation(ct))
            {
                recentFacts.Add(fact);
            }

            var fallbackIds = recentFacts.Select(f => f.Id).ToList();
            if (fallbackIds.Count > 0)
            {
                await UpdateAccessCountsAsync(fallbackIds, ct);
            }

            return recentFacts;
        }

        // Step 3: hybrid re-rank with keyword score and optional decay.
        // The vector similarity is derived from the pgvector cosine distance (0=identical, 2=opposite)
        // returned by the DB, avoiding a redundant in-process cosine computation.
        var scored = candidatesWithDistance.Select(c =>
                                           {
                                               // Convert pgvector cosine distance [0, 2] to similarity [-1, 1], clamped to [0, 1]
                                               float vecScore = Math.Clamp(1f - (float)c.Distance, 0f, 1f);
                                               float keywordScore = 0f;
                                               if (c.Fact.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                                               {
                                                   keywordScore = 1f;
                                               }
                                               float hybrid = vecScore * VectorWeight + keywordScore * KeywordWeight;

                                               if (_halfLifeDays > 0)
                                               {
                                                   hybrid = MemoryDecayScoring.ApplyDecayWithUsage(
                                                       hybrid, c.Fact.CreatedAt, _halfLifeDays, c.Fact.AccessCount, c.Fact.LastAccessedAt);
                                               }

                                               return (c.Fact, hybrid);
                                           })
                                           .OrderByDescending(x => x.hybrid)
                                           .Take(topK)
                                           .Select(x => x.Fact)
                                           .ToList();

        var returnedIds = scored.Select(f => f.Id).ToList();
        if (returnedIds.Count > 0)
        {
            await UpdateAccessCountsAsync(returnedIds, ct);
        }

        return scored;
    }

    /// <summary>
    /// Legacy fallback: tsquery pre-filter + in-process cosine scoring (used when pgvector is not available).
    /// </summary>
    private async Task<IReadOnlyList<Fact>> SearchHybridFallbackAsync(
        string query, float[] queryEmbedding, int topK, PostgresMemoryContext context, CancellationToken ct)
    {
        List<FactWithEmbedding> rows;
        try
        {
            var tsquerySql =
                $$"""
                SELECT "Id", "Content", "CreatedAt",
                       "{{Fact.AccessCountColumn}}" AS "AccessCount", "{{Fact.LastAccessedAtColumn}}" AS "LastAccessedAt",
                       embedding AS "EmbeddingJson"
                FROM "{{Fact.TableName}}"
                WHERE content_tsv @@ websearch_to_tsquery('simple', {0})
                ORDER BY ts_rank(content_tsv, websearch_to_tsquery('simple', {1})) DESC
                LIMIT {{CandidateLimit}}
                """;

            rows = await context.Database
                                .SqlQueryRaw<FactWithEmbedding>(tsquerySql, query, query)
                                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                FormattableString fallbackSql =
                    $"""
                    SELECT "Id", "Content", "CreatedAt",
                           "{Fact.AccessCountColumn}" AS "AccessCount", "{Fact.LastAccessedAtColumn}" AS "LastAccessedAt",
                           embedding AS "EmbeddingJson"
                    FROM "{Fact.TableName}" ORDER BY "Id" DESC LIMIT {CandidateLimit}
                    """;

                rows = await context.Database
                                    .SqlQuery<FactWithEmbedding>(fallbackSql)
                                    .ToListAsync(ct);
            }
        }
        catch (Exception ex)
        {
            LogMemoryOperationFailed(logger, ex, ex.Message);
            var fallbackSql =
                $"""
                SELECT "Id", "Content", "CreatedAt",
                       "{Fact.AccessCountColumn}" AS "AccessCount", "{Fact.LastAccessedAtColumn}" AS "LastAccessedAt",
                       embedding AS "EmbeddingJson"
                FROM "{Fact.TableName}" ORDER BY "Id" DESC LIMIT {CandidateLimit}
                """;

            rows = await context.Database
                                .SqlQueryRaw<FactWithEmbedding>(fallbackSql)
                                .ToListAsync(ct);
        }

        var scored = rows.Select(row =>
                         {
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
                             float hybrid = vectorScore * VectorWeight + keywordScore * KeywordWeight;

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
        await using var context = await contextFactory.CreateDbContextAsync(ct);
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
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE \"{Fact.TableName}\"", ct);
    }

    public async Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var cutoff = DateTimeOffset.UtcNow - maxAge;

        return await context.Facts
                            .Where(f => f.CreatedAt < cutoff)
                            .ExecuteDeleteAsync(ct);
    }

    private async Task UpdateAccessCountsAsync(List<long> ids, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await contextFactory.CreateDbContextAsync(ct);
            var now = DateTimeOffset.UtcNow;
            await ctx.Facts
                     .Where(f => ids.Contains(f.Id))
                     .ExecuteUpdateAsync(s => s
                         .SetProperty(f => f.AccessCount, f => f.AccessCount + 1)
                         .SetProperty(f => f.LastAccessedAt, now), ct);
        }
        catch (Exception ex)
        {
            // Non-fatal — access tracking failure does not affect search results
            LogMemoryOperationFailed(logger, ex, ex.Message);
        }
    }

    /// <summary>Escape LIKE metacharacters for PostgreSQL (backslash escape convention).</summary>
    private static string EscapeLikePattern(string query) =>
        query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

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
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        // MigrateAsync applies pending migrations. For existing databases originally created via
        // EnsureCreated, the __EFMigrationsHistory table will be created and the initial migration
        // marked as applied if the schema already matches.
        using var migrationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        migrationCts.CancelAfter(TimeSpan.FromSeconds(30));
        await context.Database.MigrateAsync(migrationCts.Token);

        // Add content_tsv generated column + GIN index if not already present
        const string contentTsvSql =
            $"""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = '{Fact.TableName}' AND column_name = 'content_tsv'
                ) THEN
                    ALTER TABLE "{Fact.TableName}"
                        ADD COLUMN content_tsv tsvector
                            GENERATED ALWAYS AS (to_tsvector('simple', "{Fact.ContentColumn}")) STORED;
                    CREATE INDEX IF NOT EXISTS facts_tsv_idx ON "{Fact.TableName}" USING GIN(content_tsv);
                END IF;
            END $$;
            """;

        await context.Database.ExecuteSqlRawAsync(contentTsvSql);

        // Add legacy TEXT embedding column if not already present (backward compatibility)
        const string embeddingColumnSql =
            $"""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = '{Fact.TableName}' AND column_name = 'embedding'
                ) THEN
                    ALTER TABLE "{Fact.TableName}" ADD COLUMN embedding TEXT;
                END IF;
            END $$;
            """;

        await context.Database.ExecuteSqlRawAsync(embeddingColumnSql);

        // Add access tracking columns if not already present
        const string accessTrackingSql =
            $"""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = '{Fact.TableName}' AND column_name = '{Fact.AccessCountColumn}'
                ) THEN
                    ALTER TABLE "{Fact.TableName}" ADD COLUMN "{Fact.AccessCountColumn}" INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE "{Fact.TableName}" ADD COLUMN "{Fact.LastAccessedAtColumn}" TIMESTAMPTZ;
                END IF;
            END $$;
            """;

        await context.Database.ExecuteSqlRawAsync(accessTrackingSql, cancellationToken: migrationCts.Token);

        // pgvector: install extension + add vector column + HNSW index
        var dim = _embeddingDimension;
        try
        {
            await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector", cancellationToken: migrationCts.Token);
            
            var pgvecDdl = string.Create(null, stackalloc char[512],
                $"""
                 DO $$
                 BEGIN
                     IF NOT EXISTS (
                         SELECT 1 FROM information_schema.columns
                         WHERE table_name = '{Fact.TableName}' AND column_name = 'embedding_pgvec'
                     ) THEN
                         ALTER TABLE "{Fact.TableName}" ADD COLUMN embedding_pgvec vector({dim});
                         CREATE INDEX IF NOT EXISTS facts_embedding_hnsw_idx
                             ON "{Fact.TableName}" USING hnsw (embedding_pgvec vector_cosine_ops)
                             WITH (m = 16, ef_construction = 64);
                     END IF;
                 END $$;
                 """);
            
            await context.Database.ExecuteSqlRawAsync(pgvecDdl, cancellationToken: migrationCts.Token);
            _pgvectorAvailable = true;
            LogPgvectorLoaded(logger, dim);
        }
        catch (Exception ex)
        {
            _pgvectorAvailable = false;
            LogPgvectorNotAvailable(logger, ex, ex.Message);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Memory operation failed: {Message}")]
    private static partial void LogMemoryOperationFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "pgvector extension loaded; HNSW index active on embedding_pgvec vector({Dim})")]
    private static partial void LogPgvectorLoaded(ILogger logger, int dim);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message =
            "pgvector extension not available (install with: CREATE EXTENSION vector); falling back to in-process cosine scoring: {Message}")]
    private static partial void LogPgvectorNotAvailable(ILogger logger, Exception exception, string message);
}