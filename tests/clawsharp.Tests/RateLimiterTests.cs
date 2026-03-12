using Clawsharp.Core.Services;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Tests;

public sealed class RateLimiterTests
{
    private static RateLimiter CreateLimiter(int maxRequests = 3, int windowSeconds = 60)
    {
        var defaults = new AgentDefaults
        {
            RateLimitRequests = maxRequests,
            RateLimitWindowSeconds = windowSeconds
        };
        return new RateLimiter(Options.Create(defaults));
    }

    [Test]
    public void TryAcquire_WithinLimit_AllCallsReturnTrue()
    {
        var limiter = CreateLimiter(maxRequests: 3);

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeTrue();
    }

    [Test]
    public void TryAcquire_ExactlyAtLimit_NextCallReturnsFalse()
    {
        var limiter = CreateLimiter(maxRequests: 3);

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeTrue(); // 3rd call at limit
        limiter.TryAcquire("alice").ShouldBeFalse(); // 4th call exceeds limit
    }

    [Test]
    public void TryAcquire_RateLimited_SubsequentCallsReturnFalse()
    {
        var limiter = CreateLimiter(maxRequests: 2);

        limiter.TryAcquire("alice");
        limiter.TryAcquire("alice");

        limiter.TryAcquire("alice").ShouldBeFalse();
        limiter.TryAcquire("alice").ShouldBeFalse();
    }

    [Test]
    public void TryAcquire_DifferentKeys_AreIndependent()
    {
        var limiter = CreateLimiter(maxRequests: 1);

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeFalse(); // alice is rate-limited

        limiter.TryAcquire("bob").ShouldBeTrue(); // bob is not affected
    }

    [Test]
    public async Task TryAcquire_WindowExpiry_AllowsRequestsAgain()
    {
        var defaults = new AgentDefaults
        {
            RateLimitRequests = 1,
            RateLimitWindowSeconds = 1 // 1 second window
        };
        var shortLimiter = new RateLimiter(Options.Create(defaults));

        shortLimiter.TryAcquire("alice").ShouldBeTrue();
        shortLimiter.TryAcquire("alice").ShouldBeFalse();

        // Wait for the window to expire
        await Task.Delay(1200);

        shortLimiter.TryAcquire("alice").ShouldBeTrue();
    }

    [Test]
    public void TryAcquire_ZeroRateLimitRequests_DefaultsTo20()
    {
        var limiter = CreateLimiter(maxRequests: 0);

        for (var i = 0; i < 20; i++)
        {
            limiter.TryAcquire("alice").ShouldBeTrue();
        }

        limiter.TryAcquire("alice").ShouldBeFalse(); // 21st should fail
    }

    [Test]
    public void TryAcquire_NegativeRateLimitRequests_DefaultsTo20()
    {
        var limiter = CreateLimiter(maxRequests: -5);

        for (var i = 0; i < 20; i++)
        {
            limiter.TryAcquire("alice").ShouldBeTrue();
        }

        limiter.TryAcquire("alice").ShouldBeFalse();
    }

    [Test]
    public void TryAcquire_ZeroWindowSeconds_DefaultsTo60()
    {
        var defaults = new AgentDefaults
        {
            RateLimitRequests = 1,
            RateLimitWindowSeconds = 0
        };
        var limiter = new RateLimiter(Options.Create(defaults));

        limiter.TryAcquire("alice").ShouldBeTrue();
        limiter.TryAcquire("alice").ShouldBeFalse(); // blocked within 60s window
    }
}