using Clawsharp.Config.Memory;
using Clawsharp.Memory.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;

namespace Clawsharp.Memory.Redis;

/// <summary>
///     Redis-backed memory store using RediSearch for full-text and optional vector search.
///     Facts are stored as Redis hashes with a RediSearch FT index for keyword queries.
///     When an embedding provider is configured and Redis supports vector search (7.2+),
///     HNSW vector indexing is used for hybrid semantic + keyword search.
/// </summary>
public sealed partial class RedisMemory(
    IConnectionMultiplexer redis,
    ILogger<RedisMemory> logger,
    IEmbeddingProvider? embeddingProvider = null,
    IOptions<MemoryConfig>? memoryConfig = null)
    : IMemory
{
    private const int CandidateLimit = 500;

    private const int RecentContentLimit = 50;

    private const int OversampleFactor = 3;

    private const float VectorWeight = 0.6f;

    private const float KeywordWeight = 0.4f;

    // Redis key prefixes
    private const string FactPrefix = "clawsharp:fact:";
    private const string FactSeqKey = "clawsharp:fact:seq";
    private const string HistoryPrefix = "clawsharp:history:";
    private const string HistorySeqKey = "clawsharp:history:seq";
    private const string IndexName = "clawsharp:idx:facts";

    // Hash field names
    private const string ContentField = "content";
    private const string CreatedAtField = "createdAt";
    private const string AccessCountField = "accessCount";
    private const string LastAccessedAtField = "lastAccessedAt";
    private const string EmbeddingField = "embedding";
    private const string SummaryField = "summary";
    private const string TsField = "ts";

    private readonly double _halfLifeDays = memoryConfig?.Value.Decay?.HalfLifeDays ?? 0;

    private readonly int _embeddingDimension = memoryConfig?.Value.EmbeddingDimension ?? 1536;

    private volatile Task? _initTask;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>Whether vector search is available in the RediSearch index.</summary>
    private volatile bool _vectorSearchEnabled;

    public async Task<string?> GetContextAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();
        var ft = db.FT();

        var facts = new List<string>();
        try
        {
            // Use FT.SEARCH with wildcard to get facts (unordered — FT.SEARCH does not guarantee sort order)
            var query = new Query("*")
                .Limit(0, RecentContentLimit)
                .ReturnFields(ContentField)
                .Dialect(2);

            var result = await ft.SearchAsync(IndexName, query);
            foreach (var doc in result.Documents)
            {
                var content = (string?)doc[ContentField];
                if (content is not null)
                {
                    facts.Add($"- {content}");
                }
            }
        }
        catch (RedisServerException ex) when (ex.Message.Contains("no such index", StringComparison.OrdinalIgnoreCase))
        {
            // Index not ready — fall back to SCAN
            LogMemoryOperationFailed(logger, ex, "FT.SEARCH failed, falling back to SCAN");
            await FallbackScanFacts(db, facts, RecentContentLimit);
        }

        return facts.Count > 0 ? "## Memory\n" + string.Join("\n", facts) : null;
    }

    public async Task AppendFactAsync(string fact, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();

        var id = await db.StringIncrementAsync(FactSeqKey);
        var key = $"{FactPrefix}{id}";

        var entries = new List<HashEntry>
        {
            new(ContentField, fact),
            new(CreatedAtField, DateTimeOffset.UtcNow.ToString("O")),
            new(AccessCountField, 0),
        };

        if (embeddingProvider is not null)
        {
            try
            {
                var embedding = await embeddingProvider.EmbedAsync(fact, ct);
                var blob = EmbeddingToBlob(embedding);
                entries.Add(new HashEntry(EmbeddingField, blob));
            }
            catch (Exception ex)
            {
                // Embedding failure is non-fatal — fact is still stored without a vector
                LogMemoryOperationFailed(logger, ex, ex.Message);
            }
        }

        await db.HashSetAsync(key, entries.ToArray());
    }

    public async Task AppendHistoryAsync(string summary, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();

        var id = await db.StringIncrementAsync(HistorySeqKey);
        var key = $"{HistoryPrefix}{id}";
        await db.HashSetAsync(key,
        [
            new HashEntry(SummaryField, summary),
            new HashEntry(TsField, DateTimeOffset.UtcNow.ToString("O")),
        ]);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int n = 5, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();
        var ft = db.FT();

        try
        {
            var escaped = EscapeRediSearchQuery(query);
            var ftQuery = new Query(escaped)
                .Limit(0, n)
                .ReturnFields(ContentField)
                .Dialect(2);

            var result = await ft.SearchAsync(IndexName, ftQuery);
            var results = new List<string>();
            foreach (var doc in result.Documents)
            {
                var content = (string?)doc[ContentField];
                if (content is not null)
                {
                    results.Add(content);
                }
            }

            return results;
        }
        catch (RedisServerException ex)
        {
            // FT.SEARCH syntax error or index not ready — fall back to SCAN + Contains
            LogMemoryOperationFailed(logger, ex, ex.Message);
            return await ScanContainsSearch(db, query, n);
        }
    }

    public async Task<IReadOnlyList<Fact>> SearchHybridAsync(string query, float[]? queryEmbedding = null, int topK = 5,
                                                              CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();
        var ft = db.FT();

        // If no query embedding, fall back to text search returning Fact objects
        if (queryEmbedding is null || queryEmbedding.Length == 0)
        {
            return await SearchTextOnly(db, ft, query, topK);
        }

        // Hybrid search: combine text pre-filter with vector search
        if (_vectorSearchEnabled)
        {
            try
            {
                return await SearchHybridVectorAsync(db, ft, query, queryEmbedding, topK);
            }
            catch (Exception ex)
            {
                LogVectorSearchFailed(logger, ex, ex.Message);
            }
        }

        // Fallback: text pre-filter + in-process cosine scoring
        return await SearchHybridFallbackAsync(db, ft, query, queryEmbedding, topK);
    }

    public async Task<IReadOnlyList<Fact>> ListFactsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();

        var facts = new List<Fact>();
        var server = redis.GetServer(redis.GetEndPoints()[0]);

        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            var keyStr = key.ToString();
            // Skip the sequence key
            if (keyStr == FactSeqKey)
            {
                continue;
            }

            var id = ParseIdFromKey(keyStr, FactPrefix);
            if (id < 0)
            {
                continue;
            }

            var hash = await db.HashGetAllAsync(key);
            if (hash.Length > 0)
            {
                facts.Add(FactFromHash(id, hash));
            }
        }

        // Order newest first (by ID descending)
        facts.Sort((a, b) => b.Id.CompareTo(a.Id));
        return facts;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();
        var server = redis.GetServer(redis.GetEndPoints()[0]);

        // Delete all fact keys
        var keysToDelete = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            keysToDelete.Add(key);
        }

        if (keysToDelete.Count > 0)
        {
            await db.KeyDeleteAsync(keysToDelete.ToArray());
        }

        // Reset the sequence counter
        await db.KeyDeleteAsync(FactSeqKey);

        // History entries are WORM (write-once read-many) — never deleted.
        // They represent immutable compaction snapshots and are preserved across clears.
    }

    public async Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var db = redis.GetDatabase();
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var cutoff = DateTimeOffset.UtcNow - maxAge;

        var keysToDelete = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            var keyStr = key.ToString();
            if (keyStr == FactSeqKey)
            {
                continue;
            }

            var createdAtVal = await db.HashGetAsync(key, CreatedAtField);
            if (createdAtVal.IsNullOrEmpty)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(createdAtVal.ToString(), out var createdAt) && createdAt < cutoff)
            {
                keysToDelete.Add(key);
            }
        }

        if (keysToDelete.Count > 0)
        {
            await db.KeyDeleteAsync(keysToDelete.ToArray());
        }

        return keysToDelete.Count;
    }

    // ── Hybrid search with RediSearch vector index ───────────────────────────

    private async Task<IReadOnlyList<Fact>> SearchHybridVectorAsync(
        IDatabase db, SearchCommands ft, string query, float[] queryEmbedding, int topK)
    {
        var knn = topK * OversampleFactor;
        var blob = EmbeddingToBlob(queryEmbedding);

        // KNN vector search across all facts
        var vecQuery = new Query($"*=>[KNN {knn} @{EmbeddingField} $vec AS __score]")
            .AddParam("vec", blob)
            .ReturnFields(ContentField, CreatedAtField, AccessCountField, LastAccessedAtField, "__score")
            .SetSortBy("__score")
            .Limit(0, knn)
            .Dialect(2);

        var result = await ft.SearchAsync(IndexName, vecQuery);

        if (result.TotalResults == 0)
        {
            return [];
        }

        var scored = result.Documents
                           .Select(doc =>
                           {
                               var fact = FactFromDocument(doc);

                               // RediSearch KNN returns cosine distance (0=identical, 2=opposite)
                               float vecScore = 0f;
                               var scoreStr = (string?)doc["__score"];
                               if (scoreStr is not null && float.TryParse(scoreStr, out var distance))
                               {
                                   vecScore = 1f - (distance / 2f);
                               }

                               return (fact, hybrid: ComputeHybridScore(fact, vecScore, query));
                           })
                           .OrderByDescending(x => x.hybrid)
                           .Take(topK)
                           .Select(x => x.fact)
                           .ToList();

        var returnedIds = scored.Select(f => f.Id).ToList();
        if (returnedIds.Count > 0)
        {
            await UpdateAccessCountsAsync(db, returnedIds);
        }

        return scored;
    }

    /// <summary>
    ///     Fallback hybrid search: text pre-filter + in-process cosine scoring.
    ///     Used when vector indexing is not available.
    /// </summary>
    private async Task<IReadOnlyList<Fact>> SearchHybridFallbackAsync(
        IDatabase db, SearchCommands ft, string query, float[] queryEmbedding, int topK)
    {
        // Step 1: get candidate facts (text pre-filter or recent)
        List<(Fact fact, byte[]? embeddingBlob)> candidates;
        try
        {
            var escaped = EscapeRediSearchQuery(query);
            var ftQuery = new Query(escaped)
                .Limit(0, CandidateLimit)
                .ReturnFields(ContentField, CreatedAtField, AccessCountField, LastAccessedAtField, EmbeddingField)
                .Dialect(2);

            var result = await ft.SearchAsync(IndexName, ftQuery);
            candidates = result.Documents
                               .Select(doc => (FactFromDocument(doc), EmbeddingBlobFromDocument(doc)))
                               .ToList();

            // If text search returned nothing, fall back to most recent facts
            if (candidates.Count == 0)
            {
                candidates = await LoadRecentFactsWithEmbeddings(db, CandidateLimit);
            }
        }
        catch (Exception ex)
        {
            LogMemoryOperationFailed(logger, ex, ex.Message);
            candidates = await LoadRecentFactsWithEmbeddings(db, CandidateLimit);
        }

        // Step 2: in-process cosine scoring
        var scored = candidates.Select(c =>
                                {
                                    float vectorScore = 0f;
                                    if (c.embeddingBlob is not null)
                                    {
                                        var vec = BlobToEmbedding(c.embeddingBlob);
                                        if (vec.Length > 0 && vec.Length == queryEmbedding.Length)
                                        {
                                            vectorScore = EmbeddingMath.CosineSimilarity(queryEmbedding, vec);
                                        }
                                    }

                                    return (c.fact, hybrid: ComputeHybridScore(c.fact, vectorScore, query));
                                })
                                .OrderByDescending(x => x.hybrid)
                                .Take(topK)
                                .Select(x => x.fact)
                                .ToList();

        var returnedIds = scored.Select(f => f.Id).ToList();
        if (returnedIds.Count > 0)
        {
            await UpdateAccessCountsAsync(db, returnedIds);
        }

        return scored;
    }

    /// <summary>Text-only search returning Fact objects (no embedding path).</summary>
    private async Task<IReadOnlyList<Fact>> SearchTextOnly(IDatabase db, SearchCommands ft, string query, int topK)
    {
        List<Fact> candidates;
        try
        {
            var escaped = EscapeRediSearchQuery(query);
            var ftQuery = new Query(escaped)
                .Limit(0, topK)
                .ReturnFields(ContentField, CreatedAtField, AccessCountField, LastAccessedAtField)
                .Dialect(2);

            var result = await ft.SearchAsync(IndexName, ftQuery);
            candidates = result.Documents.Select(FactFromDocument).ToList();
        }
        catch (Exception ex)
        {
            LogMemoryOperationFailed(logger, ex, ex.Message);
            // Fall back to SCAN + Contains
            candidates = await ScanContainsSearchFacts(db, query, topK);
        }

        var ids = candidates.Select(f => f.Id).ToList();
        if (ids.Count > 0)
        {
            await UpdateAccessCountsAsync(db, ids);
        }

        return candidates;
    }

    /// <summary>
    ///     Computes a hybrid relevance score combining vector similarity, keyword match,
    ///     and optional time-decay with usage weighting.
    /// </summary>
    private float ComputeHybridScore(Fact fact, float vectorScore, string query)
    {
        float keywordScore = fact.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        float hybrid = vectorScore * VectorWeight + keywordScore * KeywordWeight;
        if (_halfLifeDays > 0)
        {
            hybrid = MemoryDecayScoring.ApplyDecayWithUsage(hybrid, fact.CreatedAt, _halfLifeDays, fact.AccessCount, fact.LastAccessedAt);
        }

        return hybrid;
    }

    // ── Fallback helpers ────────────────────────────────────────────────────

    private async Task FallbackScanFacts(IDatabase db, List<string> results, int limit)
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var facts = new List<(long id, string content)>();

        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            var keyStr = key.ToString();
            if (keyStr == FactSeqKey)
            {
                continue;
            }

            var id = ParseIdFromKey(keyStr, FactPrefix);
            if (id < 0)
            {
                continue;
            }

            var content = await db.HashGetAsync(key, ContentField);
            if (!content.IsNullOrEmpty)
            {
                facts.Add((id, content.ToString()));
            }
        }

        foreach (var (_, content) in facts.OrderByDescending(f => f.id).Take(limit))
        {
            results.Add($"- {content}");
        }
    }

    private async Task<List<string>> ScanContainsSearch(IDatabase db, string query, int n)
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var results = new List<string>();

        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            if (key.ToString() == FactSeqKey)
            {
                continue;
            }

            var content = await db.HashGetAsync(key, ContentField);
            if (!content.IsNullOrEmpty && content.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(content.ToString());
                if (results.Count >= n)
                {
                    break;
                }
            }
        }

        return results;
    }

    private async Task<List<Fact>> ScanContainsSearchFacts(IDatabase db, string query, int n)
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var results = new List<Fact>();

        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            var keyStr = key.ToString();
            if (keyStr == FactSeqKey)
            {
                continue;
            }

            var id = ParseIdFromKey(keyStr, FactPrefix);
            if (id < 0)
            {
                continue;
            }

            var hash = await db.HashGetAllAsync(key);
            if (hash.Length == 0)
            {
                continue;
            }

            var fact = FactFromHash(id, hash);
            if (fact.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(fact);
                if (results.Count >= n)
                {
                    break;
                }
            }
        }

        return results;
    }

    private async Task<List<(Fact fact, byte[]? embeddingBlob)>> LoadRecentFactsWithEmbeddings(IDatabase db, int limit)
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var all = new List<(long id, Fact fact, byte[]? blob)>();

        await foreach (var key in server.KeysAsync(pattern: $"{FactPrefix}*"))
        {
            var keyStr = key.ToString();
            if (keyStr == FactSeqKey)
            {
                continue;
            }

            var id = ParseIdFromKey(keyStr, FactPrefix);
            if (id < 0)
            {
                continue;
            }

            var hash = await db.HashGetAllAsync(key);
            if (hash.Length == 0)
            {
                continue;
            }

            var fact = FactFromHash(id, hash);
            byte[]? blob = null;
            var embVal = hash.FirstOrDefault(h => h.Name == EmbeddingField).Value;
            if (!embVal.IsNull)
            {
                blob = (byte[]?)embVal;
            }

            all.Add((id, fact, blob));
        }

        return all.OrderByDescending(x => x.id)
                  .Take(limit)
                  .Select(x => (x.fact, x.blob))
                  .ToList();
    }

    // ── Access tracking ─────────────────────────────────────────────────────

    private async Task UpdateAccessCountsAsync(IDatabase db, List<long> ids)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var id in ids)
            {
                var key = $"{FactPrefix}{id}";
                await db.HashIncrementAsync(key, AccessCountField);
                await db.HashSetAsync(key, [new HashEntry(LastAccessedAtField, now)]);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — access tracking failure does not affect search results
            LogMemoryOperationFailed(logger, ex, ex.Message);
        }
    }

    // ── Initialization ──────────────────────────────────────────────────────

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

            var task = InitIndexAsync();
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

    private async Task InitIndexAsync()
    {
        var db = redis.GetDatabase();
        var ft = db.FT();

        try
        {
            // Check if index already exists
            await ft.InfoAsync(IndexName);
            // Index exists — check if vector field is present
            _vectorSearchEnabled = embeddingProvider is not null;
            LogInitialized(logger, _vectorSearchEnabled);
            return;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Unknown index name", StringComparison.OrdinalIgnoreCase)
                                               || ex.Message.Contains("Unknown Index name", StringComparison.OrdinalIgnoreCase)
                                               || ex.Message.Contains("no such index", StringComparison.OrdinalIgnoreCase))
        {
            // Index does not exist — create it
        }

        var schema = new Schema()
            .AddTextField(ContentField, 1.0)
            .AddTagField(CreatedAtField, sortable: true)
            .AddNumericField(AccessCountField, sortable: true);

        // Add vector field if embedding provider is configured
        if (embeddingProvider is not null)
        {
            try
            {
                var dim = _embeddingDimension;
                var vecAttrs = new Dictionary<string, object>
                {
                    ["TYPE"] = "FLOAT32",
                    ["DIM"] = dim,
                    ["DISTANCE_METRIC"] = "COSINE",
                };
                schema.AddVectorField(EmbeddingField, Schema.VectorField.VectorAlgo.HNSW, vecAttrs);
                _vectorSearchEnabled = true;
            }
            catch (Exception ex)
            {
                LogVectorIndexCreateFailed(logger, ex, ex.Message);
                _vectorSearchEnabled = false;
            }
        }

        var createParams = FTCreateParams.CreateParams()
            .On(IndexDataType.HASH)
            .Prefix(FactPrefix);

        try
        {
            await ft.CreateAsync(IndexName, createParams, schema);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Index already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Race condition — another instance created the index first; ignore
        }

        LogInitialized(logger, _vectorSearchEnabled);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Fact FactFromHash(long id, HashEntry[] entries)
    {
        var content = "";
        var createdAt = DateTimeOffset.UtcNow;
        var accessCount = 0;
        DateTimeOffset? lastAccessedAt = null;

        foreach (var entry in entries)
        {
            switch (entry.Name.ToString())
            {
                case ContentField:
                    content = entry.Value.ToString();
                    break;
                case CreatedAtField:
                    if (DateTimeOffset.TryParse(entry.Value.ToString(), out var ca))
                    {
                        createdAt = ca;
                    }

                    break;
                case AccessCountField:
                    if (entry.Value.TryParse(out int ac))
                    {
                        accessCount = ac;
                    }

                    break;
                case LastAccessedAtField:
                    if (!entry.Value.IsNullOrEmpty && DateTimeOffset.TryParse(entry.Value.ToString(), out var la))
                    {
                        lastAccessedAt = la;
                    }

                    break;
            }
        }

        return new Fact
        {
            Id = id,
            Content = content,
            CreatedAt = createdAt,
            AccessCount = accessCount,
            LastAccessedAt = lastAccessedAt,
        };
    }

    private static Fact FactFromDocument(Document doc)
    {
        var id = ParseIdFromKey(doc.Id, FactPrefix);

        var content = (string?)doc[ContentField] ?? "";

        var createdAt = DateTimeOffset.UtcNow;
        var createdAtStr = (string?)doc[CreatedAtField];
        if (createdAtStr is not null && DateTimeOffset.TryParse(createdAtStr, out var ca))
        {
            createdAt = ca;
        }

        var accessCount = 0;
        var accessCountStr = (string?)doc[AccessCountField];
        if (accessCountStr is not null && int.TryParse(accessCountStr, out var ac))
        {
            accessCount = ac;
        }

        DateTimeOffset? lastAccessedAt = null;
        var lastAccessedAtStr = (string?)doc[LastAccessedAtField];
        if (lastAccessedAtStr is not null && DateTimeOffset.TryParse(lastAccessedAtStr, out var la))
        {
            lastAccessedAt = la;
        }

        return new Fact
        {
            Id = id,
            Content = content,
            CreatedAt = createdAt,
            AccessCount = accessCount,
            LastAccessedAt = lastAccessedAt,
        };
    }

    private static byte[]? EmbeddingBlobFromDocument(Document doc)
    {
        var val = doc[EmbeddingField];
        if (val == RedisValue.Null || val.IsNullOrEmpty)
        {
            return null;
        }

        return (byte[]?)val;
    }

    private static long ParseIdFromKey(string key, string prefix)
    {
        var idStr = key.StartsWith(prefix, StringComparison.Ordinal)
            ? key[prefix.Length..]
            : key;

        return long.TryParse(idStr, out var id) ? id : -1;
    }

    /// <summary>
    ///     Escape special RediSearch query characters and wrap each word in quotes for exact matching.
    /// </summary>
    private static string EscapeRediSearchQuery(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "*";
        }

        // Wrap each word in double quotes to treat as literal term, escaping inner quotes
        return string.Join(" ", words.Select(w => $"\"{w.Replace("\"", "")}\""));
    }

    /// <summary>Convert a float[] embedding to a byte[] blob for Redis VECTOR field storage.</summary>
    private static byte[] EmbeddingToBlob(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Convert a byte[] blob back to a float[] embedding.</summary>
    private static float[] BlobToEmbedding(byte[] blob)
    {
        var floats = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
        return floats;
    }

    // ── LoggerMessage definitions ───────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Redis memory initialized (vector search: {VectorEnabled})")]
    private static partial void LogInitialized(ILogger logger, bool vectorEnabled);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Redis memory operation failed: {Message}")]
    private static partial void LogMemoryOperationFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Redis vector search failed, falling back to text + in-process cosine: {Message}")]
    private static partial void LogVectorSearchFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Failed to create vector index in RediSearch; vector search disabled: {Message}")]
    private static partial void LogVectorIndexCreateFailed(ILogger logger, Exception exception, string message);
}
