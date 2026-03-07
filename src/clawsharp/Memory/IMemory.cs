using Clawsharp.Memory.Entities;

namespace Clawsharp.Memory;

/// <summary>
///     Abstraction for the memory subsystem (facts + conversation history).
/// </summary>
public interface IMemory
{
    Task<string?> GetContextAsync(CancellationToken ct = default);

    Task AppendFactAsync(string fact, CancellationToken ct = default);

    Task AppendHistoryAsync(string summary, CancellationToken ct = default);

    Task<IReadOnlyList<string>> SearchAsync(string query, int n = 5, CancellationToken ct = default);

    /// <summary>Returns all stored facts, ordered newest first.</summary>
    Task<IReadOnlyList<Fact>> ListFactsAsync(CancellationToken ct = default);

    /// <summary>Deletes all facts. History entries are WORM and preserved across clears.</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    ///     Search facts using hybrid LIKE + vector similarity.
    ///     Falls back to LIKE-only if no embedding provider is configured.
    /// </summary>
    Task<IReadOnlyList<Fact>> SearchHybridAsync(string query, float[]? queryEmbedding = null, int topK = 5, CancellationToken ct = default);

    /// <summary>
    ///     Deletes facts older than <paramref name="maxAge"/>.
    ///     Returns the number of facts pruned.
    /// </summary>
    Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default);
}