using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Core.Services;

/// <summary>
///     Simple per-key sliding-window rate limiter. Thread-safe.
///     Uses a plain <see cref="Dictionary{TKey,TValue}" /> of timestamp queues
///     protected by a single lock to enforce a maximum number of requests
///     per rolling time window (eliminates TOCTOU risk of ConcurrentDictionary).
///     A secondary per-IP layer prevents attackers from bypassing per-session
///     limits by creating multiple sessions from the same IP address.
///     Stale buckets are scavenged periodically to prevent unbounded memory growth.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _buckets = new();

    /// <summary>
    ///     Separate bucket dictionary for IP-based rate limiting.
    ///     Each dictionary instance serves as its own lock object.
    /// </summary>
    private readonly Dictionary<string, Queue<DateTimeOffset>> _ipBuckets = new();

    private readonly int _maxRequests;

    /// <summary>
    ///     Per-IP rate limit is 5x the per-session limit. This is a secondary defense —
    ///     legitimate users with multiple sessions from the same IP won't hit it,
    ///     but an attacker spawning many sessions from one IP will.
    /// </summary>
    internal const int IpLimitMultiplier = 5;

    private readonly int _maxIpRequests;

    private readonly TimeSpan _window;

    private readonly Timer _scavengeTimer;

    public RateLimiter(IOptions<AgentDefaults> defaultsOptions)
    {
        var defaults = defaultsOptions.Value;
        _maxRequests = defaults.RateLimitRequests > 0 ? defaults.RateLimitRequests : 20;
        _maxIpRequests = _maxRequests * IpLimitMultiplier;
        _window = TimeSpan.FromSeconds(defaults.RateLimitWindowSeconds > 0 ? defaults.RateLimitWindowSeconds : 60);

        // Scavenge stale buckets every 5 minutes to prevent unbounded memory growth
        // from transient IPs or sessions that never return.
        _scavengeTimer = new Timer(_ => Scavenge(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    ///     Returns <c>true</c> if the request is allowed, <c>false</c> if rate-limited.
    /// </summary>
    public bool TryAcquire(string key)
    {
        return TryAcquireCore(_buckets, key, _maxRequests);
    }

    /// <summary>
    ///     Secondary per-IP rate limiter. Returns <c>true</c> if the request is allowed,
    ///     <c>false</c> if the IP has exceeded the aggregate limit (5x per-session limit).
    ///     When <paramref name="ipAddress"/> is <c>null</c> (e.g. CLI channel), returns <c>true</c>.
    /// </summary>
    public bool TryAcquireByIp(string? ipAddress)
    {
        if (ipAddress is null)
        {
            return true;
        }

        return TryAcquireCore(_ipBuckets, ipAddress, _maxIpRequests);
    }

    /// <summary>
    ///     Core sliding-window algorithm shared by both per-session and per-IP checks.
    /// </summary>
    private bool TryAcquireCore(Dictionary<string, Queue<DateTimeOffset>> buckets, string key, int maxRequests)
    {
        var now = DateTimeOffset.UtcNow;

        lock (buckets)
        {
            if (!buckets.TryGetValue(key, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                buckets[key] = queue;
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
                buckets.Remove(key);
            }

            if (queue.Count >= maxRequests)
            {
                return false;
            }

            queue.Enqueue(now);
            buckets[key] = queue; // Re-add if removed above
            return true;
        }
    }

    /// <summary>
    ///     Periodic scavenger that sweeps both dictionaries for entries with all
    ///     timestamps older than the window. Prevents unbounded memory growth from
    ///     transient IPs or sessions that send one request and never return.
    /// </summary>
    private void Scavenge()
    {
        ScavengeBuckets(_buckets);
        ScavengeBuckets(_ipBuckets);
    }

    private void ScavengeBuckets(Dictionary<string, Queue<DateTimeOffset>> buckets)
    {
        var now = DateTimeOffset.UtcNow;
        lock (buckets)
        {
            var stale = new List<string>();
            foreach (var (key, queue) in buckets)
            {
                while (queue.Count > 0 && now - queue.Peek() > _window)
                {
                    queue.Dequeue();
                }

                if (queue.Count == 0)
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                buckets.Remove(key);
            }
        }
    }

    public void Dispose() => _scavengeTimer.Dispose();
}