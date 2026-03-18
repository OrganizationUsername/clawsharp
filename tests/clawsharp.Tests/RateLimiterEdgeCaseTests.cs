using System.Net;
using Clawsharp.Core.Services;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Tests;

/// <summary>
/// Edge-case tests for <see cref="RateLimiter"/>:
/// MED-26 (WindowSeconds=0 behavior), negative window, and concurrent access.
/// </summary>
[TestFixture]
public sealed class RateLimiterEdgeCaseTests
{
    private static RateLimiter CreateLimiter(int maxRequests = 3, int windowSeconds = 60)
    {
        var defaults = new AgentDefaults
        {
            RateLimitRequests = maxRequests,
            RateLimitWindow = TimeSpan.FromSeconds(windowSeconds)
        };
        return new RateLimiter(Options.Create(defaults));
    }

    // ── MED-26: WindowSeconds=0 ─────────────────────────────────────────

    [Test]
    public void TryAcquire_ZeroWindow_DefaultsTo60Seconds_OnlyNRequestsAllowed()
    {
        // When WindowSeconds=0, the constructor defaults to 60 seconds.
        // With maxRequests=2 and a 60-second window, only 2 requests are allowed.
        var limiter = CreateLimiter(maxRequests: 2, windowSeconds: 0);

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeFalse(
            "With windowSeconds=0 (defaulting to 60s), the 3rd request should be denied");
    }

    [Test]
    public void TryAcquire_NegativeWindow_DefaultsTo60Seconds()
    {
        // Negative windowSeconds should also default to 60
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: -10);

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeFalse(
            "Negative windowSeconds should default to 60s window");
    }

    // ── Very short window ───────────────────────────────────────────────

    [Test]
    public async Task TryAcquire_OneSecondWindow_ExpiresQuickly()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 1);

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeFalse();

        // Wait for window to expire
        await Task.Delay(1200);

        limiter.TryAcquire("alice").ShouldBeTrue("Request should be allowed after window expiry");
    }

    // ── Very high maxRequests ───────────────────────────────────────────

    [Test]
    public void TryAcquire_HighLimit_AllowsManyRequests()
    {
        var limiter = CreateLimiter(maxRequests: 10_000, windowSeconds: 60);

        for (var i = 0; i < 10_000; i++)
        {
            limiter.TryAcquire("burst").ShouldBeTrue();
        }

        limiter.TryAcquire("burst").ShouldBeFalse("10,001st request should be denied");
    }

    // ── Concurrent access safety ────────────────────────────────────────

    [Test]
    public void TryAcquire_ConcurrentAccess_DoesNotExceedLimit()
    {
        // The rate limiter uses lock(buckets) so it should be thread-safe.
        // Verify that concurrent calls don't allow more than maxRequests.
        var limiter = CreateLimiter(maxRequests: 50, windowSeconds: 60);
        var allowed = 0;

        Parallel.For(0, 200, _ =>
        {
            if (limiter.TryAcquire("concurrent"))
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.ShouldBe(50, "Concurrent calls should not exceed the rate limit");
    }

    [Test]
    public void TryAcquireByIp_ConcurrentAccess_DoesNotExceedLimit()
    {
        var limiter = CreateLimiter(maxRequests: 10, windowSeconds: 60);
        // IP limit = 10 * 5 = 50
        var ip = IPAddress.Parse("10.0.0.1");
        var allowed = 0;

        Parallel.For(0, 200, _ =>
        {
            if (limiter.TryAcquireByIp(ip))
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.ShouldBe(50, "Concurrent IP calls should not exceed the IP rate limit");
    }

    // ── Empty key ───────────────────────────────────────────────────────

    [Test]
    public void TryAcquire_EmptyKey_StillRateLimited()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 60);

        limiter.TryAcquire("").ShouldBeTrue();
        limiter.TryAcquire("").ShouldBeFalse("Empty string key should still be rate-limited");
    }

    // ── Dispose ─────────────────────────────────────────────────────────

    [Test]
    public void Dispose_DoesNotThrow()
    {
        var limiter = CreateLimiter();
        limiter.TryAcquire("test");

        Should.NotThrow(() => limiter.Dispose());
    }

    [Test]
    public void Dispose_MultipleCallsDoNotThrow()
    {
        var limiter = CreateLimiter();

        Should.NotThrow(() =>
        {
            limiter.Dispose();
            limiter.Dispose();
        });
    }

    // ── Per-IP with maxRequests=1 ───────────────────────────────────────

    [Test]
    public void TryAcquireByIp_MaxRequests1_IpLimitIs5()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 60);
        var ip = IPAddress.Parse("10.0.0.1");

        // IP limit = 1 * 5 = 5
        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquireByIp(ip).ShouldBeTrue();
        }

        limiter.TryAcquireByIp(ip).ShouldBeFalse("6th IP request should be denied (limit=5)");
    }

    // ── IPv6 addresses ──────────────────────────────────────────────────

    [Test]
    public void TryAcquireByIp_IPv6Address_WorksCorrectly()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 60);
        var ipv6 = IPAddress.Parse("2001:db8::1");

        // IP limit = 1 * 5 = 5
        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquireByIp(ipv6).ShouldBeTrue();
        }

        limiter.TryAcquireByIp(ipv6).ShouldBeFalse();
    }
}
