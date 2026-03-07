namespace Clawsharp.Channels;

/// <summary>
/// LRU-evicting deduplicator. Thread-safe via <see cref="Lock"/>.
/// Used by Nostr and Lark channels to track recently-seen message/event IDs
/// and discard duplicates without unbounded memory growth.
/// </summary>
public sealed class BoundedDeduplicator<T> where T : notnull
{
    private readonly HashSet<T> _seen;

    private readonly Queue<T> _queue = new();

    private readonly Lock _lock = new();

    private readonly int _capacity;

    public BoundedDeduplicator(int capacity, IEqualityComparer<T>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
        _seen = new HashSet<T>(comparer ?? EqualityComparer<T>.Default);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="item"/> was NOT seen before (and adds it).
    /// Returns <c>false</c> if it was already tracked (duplicate).
    /// </summary>
    public bool TryAdd(T item)
    {
        lock (_lock)
        {
            if (!_seen.Add(item))
            {
                return false;
            }

            _queue.Enqueue(item);

            // LRU eviction: remove oldest entries when over capacity
            while (_queue.Count > _capacity)
            {
                var oldest = _queue.Dequeue();
                _seen.Remove(oldest);
            }

            return true;
        }
    }
}