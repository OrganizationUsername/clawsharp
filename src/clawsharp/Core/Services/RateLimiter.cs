using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Core.Services;

/// <summary>
///     Simple per-key sliding-window rate limiter. Thread-safe.
///     Uses a plain <see cref="Dictionary{TKey,TValue}" /> of timestamp queues
///     protected by a single lock to enforce a maximum number of requests
///     per rolling time window (eliminates TOCTOU risk of ConcurrentDictionary).
/// </summary>
public sealed class RateLimiter
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _buckets = new();

    private readonly int _maxRequests;

    private readonly TimeSpan _window;

    public RateLimiter(IOptions<AgentDefaults> defaultsOptions)
    {
        var defaults = defaultsOptions.Value;
        _maxRequests = defaults.RateLimitRequests > 0 ? defaults.RateLimitRequests : 20;
        _window = TimeSpan.FromSeconds(defaults.RateLimitWindowSeconds > 0 ? defaults.RateLimitWindowSeconds : 60);
    }

    /// <summary>
    ///     Returns <c>true</c> if the request is allowed, <c>false</c> if rate-limited.
    /// </summary>
    public bool TryAcquire(string key)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_buckets)
        {
            if (!_buckets.TryGetValue(key, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _buckets[key] = queue;
            }

            // Evict timestamps outside the window
            while (queue.Count > 0 && now - queue.Peek() > _window)
            {
                queue.Dequeue();
            }

            // Remove empty buckets to prevent unbounded memory growth
            // from stale session keys that are never accessed again.
            if (queue.Count == 0)
            {
                _buckets.Remove(key);
            }

            if (queue.Count >= _maxRequests)
            {
                return false;
            }

            queue.Enqueue(now);
            _buckets[key] = queue; // Re-add if removed above
            return true;
        }
    }
}