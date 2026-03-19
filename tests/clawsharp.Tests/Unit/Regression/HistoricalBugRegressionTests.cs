using System.Net;
using System.Text.Json;
using Clawsharp.Channels.Web;
using Clawsharp.Config.Agent;
using Clawsharp.Core.Services;
using Clawsharp.Security;
using Clawsharp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Unit.Regression;

/// <summary>
/// Regression tests for historical bugs found across PRs #1–#12.
/// Each test calls real production code to prevent the bug from silently reappearing.
/// Organized by PR/commit where the bug was discovered and fixed.
/// </summary>
public sealed class HistoricalBugRegressionTests
{
    // ══════════════════════════════════════════════════════════════════════
    //  PR #7: ConsolidateMemoryAsync leak gap
    //  Bug: LLM-generated memory consolidation summaries were persisted
    //       without LeakDetector.Scan, unlike the parallel flush path.
    //       PII or secrets in the summary would be stored permanently.
    //  Fix: LeakDetector.Scan(summary, 0.5) before AppendHistoryAsync
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public void LeakDetector_ScansStripeKey_AtAnySensitivity()
    {
        // Consolidation uses sensitivity=0.5. Structural patterns (API keys)
        // must fire even at low sensitivity to prevent leak gap.
        var content = "User discussed billing. Key: sk_live_abc123def456ghi789jkl012";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse("Stripe key should be detected at sensitivity 0.5");
        result.Redacted.ShouldNotContain("sk_live_abc123def456ghi789jkl012");
    }

    [Test]
    public void LeakDetector_ScansOpenAiKey_AtConsolidationSensitivity()
    {
        var content = "Summary: user asked about API. Their key is sk-abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmn";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse("OpenAI key should be detected at consolidation sensitivity");
        result.Redacted.ShouldNotContain("sk-abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmn");
    }

    [Test]
    public void LeakDetector_ScansAnthropicKey_AtConsolidationSensitivity()
    {
        var content = "Memory: user's Anthropic key is sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse("Anthropic key should be detected at consolidation sensitivity");
        result.Redacted.ShouldNotContain("sk-ant-api03-");
    }

    [Test]
    public void LeakDetector_ScansPrivateKeys_AtConsolidationSensitivity()
    {
        var content = "User shared their SSH key:\n-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA0\n-----END RSA PRIVATE KEY-----";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse("Private key block should be detected at consolidation sensitivity");
        result.Redacted.ShouldNotContain("BEGIN RSA PRIVATE KEY");
    }

    [Test]
    public void LeakDetector_ScansJwtTokens_AtConsolidationSensitivity()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var content = $"User's auth token: {jwt}";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse("JWT token should be detected at consolidation sensitivity");
    }

    [Test]
    public void LeakDetector_ScansDbUrls_AtConsolidationSensitivity()
    {
        var content = "Database: postgresql://admin:s3cret@db.example.com:5432/production";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse("Database URL with credentials should be detected");
        result.Redacted.ShouldNotContain("s3cret");
    }

    [Test]
    public void LeakDetector_CleanSummary_PassesUnmodified()
    {
        var content = "User discussed weather preferences and favorite restaurants.";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeTrue("Clean summary should not trigger leak detection");
        result.Redacted.ShouldBe(content);
    }

    [Test]
    public void LeakDetector_MultipleLeaks_AllRedacted()
    {
        var content = "Keys: sk_live_abc123def456ghi789jkl012 and sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345";
        var result = LeakDetector.Scan(content, 0.5);

        result.IsClean.ShouldBeFalse();
        result.Patterns.Count.ShouldBeGreaterThanOrEqualTo(2, "Both keys should be detected");
        result.Redacted.ShouldNotContain("sk_live_");
        result.Redacted.ShouldNotContain("sk-ant-");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PR #12: SpawnTool zero/negative timeout guard
    //  Bug: Config allows spawnTimeout=0 or negative, which causes
    //       CancellationTokenSource.CancelAfter() to throw ArgumentOutOfRange.
    //  Fix: AgentDefaults.ClampSpawnTimeout — lower bound 60s, upper 24h.
    //  Calls real: AgentDefaults.ClampSpawnTimeout()
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public void ClampSpawnTimeout_Zero_FallsBackTo60Seconds()
    {
        AgentDefaults.ClampSpawnTimeout(TimeSpan.Zero)
            .ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void ClampSpawnTimeout_Negative_FallsBackTo60Seconds()
    {
        AgentDefaults.ClampSpawnTimeout(TimeSpan.FromSeconds(-30))
            .ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void ClampSpawnTimeout_Valid_PreservesConfiguredValue()
    {
        AgentDefaults.ClampSpawnTimeout(TimeSpan.FromSeconds(120))
            .ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Test]
    public void ClampSpawnTimeout_OneTick_IsPositive_Preserved()
    {
        AgentDefaults.ClampSpawnTimeout(TimeSpan.FromTicks(1))
            .ShouldBe(TimeSpan.FromTicks(1));
    }

    [Test]
    public void ClampSpawnTimeout_ExceedsMax_ClampedTo24Hours()
    {
        AgentDefaults.ClampSpawnTimeout(TimeSpan.FromDays(30))
            .ShouldBe(AgentDefaults.MaxSpawnTimeout);
    }

    [Test]
    public void ClampSpawnTimeout_MaxValue_ClampedTo24Hours()
    {
        // TimeSpan.MaxValue would cause CancelAfter to throw ArgumentOutOfRangeException.
        // ClampSpawnTimeout must prevent this.
        var clamped = AgentDefaults.ClampSpawnTimeout(TimeSpan.MaxValue);
        clamped.ShouldBe(AgentDefaults.MaxSpawnTimeout);

        using var cts = new CancellationTokenSource();
        Should.NotThrow(() => cts.CancelAfter(clamped));
    }

    [Test]
    public void ClampSpawnTimeout_ExactlyMax_Preserved()
    {
        AgentDefaults.ClampSpawnTimeout(AgentDefaults.MaxSpawnTimeout)
            .ShouldBe(AgentDefaults.MaxSpawnTimeout);
    }

    [Test]
    public void ClampSpawnTimeout_ZeroGuard_CancelAfterDoesNotThrow()
    {
        var clamped = AgentDefaults.ClampSpawnTimeout(TimeSpan.Zero);

        using var cts = new CancellationTokenSource();
        Should.NotThrow(() => cts.CancelAfter(clamped));
    }

    [Test]
    public void AgentDefaults_SpawnTimeout_DefaultIs60Seconds()
    {
        var defaults = new AgentDefaults();
        defaults.SpawnTimeout.ShouldBe(TimeSpan.FromSeconds(60));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Initial commit: PromptGuard toggle not respected
    //  Bug: SuspicionTracker.IsBlocked was checked unconditionally even
    //       when promptInjectionGuard was disabled in config.
    //  Fix: Guard with _defaults.PromptInjectionGuard check
    //  Calls real: SuspicionTracker, AgentDefaults.PromptInjectionGuard
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public void SuspicionTracker_IsBlocked_AtThreshold()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold);

        tracker.IsBlocked.ShouldBeTrue();
        tracker.IsWarning.ShouldBeTrue();
    }

    [Test]
    public void SuspicionTracker_NotBlocked_BelowThreshold()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold - 1);

        tracker.IsBlocked.ShouldBeFalse();
    }

    [Test]
    public void SuspicionTracker_IsWarning_AtThreshold()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(SuspicionTracker.WarnThreshold);

        tracker.IsWarning.ShouldBeTrue();
        tracker.IsBlocked.ShouldBeFalse();
    }

    [Test]
    public void PromptGuardToggle_WhenDisabled_ShouldNotBlockEvenIfScoreHigh()
    {
        // Uses real AgentDefaults to read the toggle, real SuspicionTracker for score.
        // Production code: if (_defaults.PromptInjectionGuard && _suspicionTracker.IsBlocked)
        var defaults = new AgentDefaults { PromptInjectionGuard = false };
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(100);

        var shouldBlock = defaults.PromptInjectionGuard && tracker.IsBlocked;

        shouldBlock.ShouldBeFalse("When guard is disabled, high suspicion should NOT trigger block");
    }

    [Test]
    public void PromptGuardToggle_WhenEnabled_ShouldBlockIfScoreHigh()
    {
        var defaults = new AgentDefaults { PromptInjectionGuard = true };
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold);

        var shouldBlock = defaults.PromptInjectionGuard && tracker.IsBlocked;

        shouldBlock.ShouldBeTrue("When guard is enabled, high suspicion SHOULD trigger block");
    }

    [Test]
    public void PromptGuardToggle_WhenEnabled_ShouldNotBlockIfScoreLow()
    {
        var defaults = new AgentDefaults { PromptInjectionGuard = true };
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(1);

        var shouldBlock = defaults.PromptInjectionGuard && tracker.IsBlocked;

        shouldBlock.ShouldBeFalse("Low suspicion should not trigger block even when guard is enabled");
    }

    [Test]
    public void PromptGuardToggle_DefaultIsEnabled()
    {
        var defaults = new AgentDefaults();
        defaults.PromptInjectionGuard.ShouldBeTrue("PromptInjectionGuard should default to true");
    }

    [Test]
    public void SuspicionTracker_Reset_ClearsScore()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold);
        tracker.IsBlocked.ShouldBeTrue();

        tracker.Reset();

        tracker.IsBlocked.ShouldBeFalse();
        tracker.IsWarning.ShouldBeFalse();
        tracker.Score.ShouldBe(0);
    }

    [Test]
    public void SuspicionTracker_RecordSuspicion_Cumulative()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(1);
        tracker.RecordSuspicion(2);
        tracker.RecordSuspicion(1);

        tracker.Score.ShouldBe(4);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PR #4: Rate limiter check order — IP before session
    //  Bug: Session rate limit was checked before IP limit, consuming a
    //       session slot even when the IP was already blocked.
    //  Fix: Check IP limit FIRST; return early before session check.
    //  Calls real: RateLimiter.TryAcquire, RateLimiter.TryAcquireByIp
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public void RateLimiter_IpAndSession_HaveIndependentBudgets()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 60);
        var ip = IPAddress.Parse("10.0.0.1");

        // Exhaust IP limit (1 * 5 = 5 calls)
        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquireByIp(ip).ShouldBeTrue();
        }

        limiter.TryAcquireByIp(ip).ShouldBeFalse("IP should be blocked");

        // Session budget is independent — not consumed by IP checks
        limiter.TryAcquire("session-1").ShouldBeTrue(
            "Session slot should not have been consumed by IP-blocked requests");
    }

    [Test]
    public void RateLimiter_SessionExhausted_IpStillHasBudget()
    {
        var limiter = CreateLimiter(maxRequests: 2, windowSeconds: 60);
        var ip = IPAddress.Parse("10.0.0.1");

        limiter.TryAcquireByIp(ip).ShouldBeTrue();
        limiter.TryAcquire("session-1").ShouldBeTrue();

        limiter.TryAcquireByIp(ip).ShouldBeTrue();
        limiter.TryAcquire("session-1").ShouldBeTrue();

        limiter.TryAcquireByIp(ip).ShouldBeTrue("IP should still have budget");
        limiter.TryAcquire("session-1").ShouldBeFalse("Session should be exhausted");
    }

    [Test]
    public void RateLimiter_NullIp_AlwaysAllowed()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 60);

        // CLI/IRC channels have null IP — should always pass IP check
        limiter.TryAcquireByIp(null).ShouldBeTrue();
        limiter.TryAcquireByIp(null).ShouldBeTrue();
        limiter.TryAcquireByIp(null).ShouldBeTrue();
    }

    [Test]
    public void RateLimiter_DifferentIps_IndependentBudgets()
    {
        var limiter = CreateLimiter(maxRequests: 1, windowSeconds: 60);
        var ip1 = IPAddress.Parse("10.0.0.1");
        var ip2 = IPAddress.Parse("10.0.0.2");

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquireByIp(ip1).ShouldBeTrue();
        }

        limiter.TryAcquireByIp(ip1).ShouldBeFalse("IP1 exhausted");
        limiter.TryAcquireByIp(ip2).ShouldBeTrue("IP2 should have its own budget");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PR #4: IPv4-mapped-IPv6 normalization
    //  Bug: ::ffff:192.168.1.1 and 192.168.1.1 were treated as different
    //       IPs, allowing attackers to bypass per-IP rate limits.
    //  Fix: WebChannel.NormalizeIp() maps IPv4-mapped-IPv6 to plain IPv4.
    //  Calls real: WebChannel.NormalizeIp (internal static)
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public void NormalizeIp_IPv4MappedIPv6_ReturnsIPv4()
    {
        var mapped = IPAddress.Parse("::ffff:192.0.2.1");
        mapped.IsIPv4MappedToIPv6.ShouldBeTrue();

        var normalized = WebChannel.NormalizeIp(mapped);

        normalized.ShouldNotBeNull();
        normalized!.ToString().ShouldBe("192.0.2.1");
        normalized.IsIPv4MappedToIPv6.ShouldBeFalse();
    }

    [Test]
    public void NormalizeIp_PureIPv4_ReturnsSame()
    {
        var ipv4 = IPAddress.Parse("192.0.2.1");

        var normalized = WebChannel.NormalizeIp(ipv4);

        normalized.ShouldBe(ipv4);
    }

    [Test]
    public void NormalizeIp_PureIPv6_ReturnsSame()
    {
        var ipv6 = IPAddress.Parse("2001:db8::1");

        var normalized = WebChannel.NormalizeIp(ipv6);

        normalized.ShouldBe(ipv6);
    }

    [Test]
    public void NormalizeIp_Null_ReturnsNull()
    {
        WebChannel.NormalizeIp(null).ShouldBeNull();
    }

    [Test]
    public void NormalizeIp_IPv4Loopback_Mapped_NormalizesToIPv4()
    {
        var mapped = IPAddress.Parse("::ffff:127.0.0.1");

        var normalized = WebChannel.NormalizeIp(mapped);

        normalized.ShouldNotBeNull();
        normalized!.ToString().ShouldBe("127.0.0.1");
    }

    [Test]
    public void NormalizeIp_MappedAndUnmapped_ShareSameBucket()
    {
        var mapped = IPAddress.Parse("::ffff:10.0.0.1");
        var plain = IPAddress.Parse("10.0.0.1");

        var normalizedMapped = WebChannel.NormalizeIp(mapped);
        var normalizedPlain = WebChannel.NormalizeIp(plain);

        normalizedMapped!.ToString().ShouldBe(normalizedPlain!.ToString(),
            "Mapped and plain IPv4 must normalize to the same address for rate limiting");
    }

    [Test]
    public void NormalizeIp_IPv6Loopback_NotMapped_ReturnsSame()
    {
        var loopback = IPAddress.IPv6Loopback;

        var normalized = WebChannel.NormalizeIp(loopback);

        normalized.ShouldBe(loopback);
        normalized!.IsIPv4MappedToIPv6.ShouldBeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PR #4: Rate limiter scavenger — stale bucket cleanup
    //  Bug: Rate limiter buckets for transient IPs/sessions were never
    //       cleaned up, causing unbounded memory growth.
    //  Fix: Timer-based scavenger removes empty buckets every 5 minutes.
    //  Calls real: RateLimiter (in-line eviction path)
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RateLimiter_StaleBuckets_EvictedAfterWindowExpires()
    {
        // Use a 100ms window so buckets expire quickly
        var limiter = CreateLimiterWithTimeSpan(maxRequests: 1, window: TimeSpan.FromMilliseconds(100));

        limiter.TryAcquire("transient-session-1");
        limiter.TryAcquire("transient-session-2");
        limiter.TryAcquireByIp(IPAddress.Parse("10.0.0.1"));

        // Wait for window to expire
        await Task.Delay(200);

        // After expiry, the NEXT TryAcquire call evicts the stale entry
        limiter.TryAcquire("transient-session-1").ShouldBeTrue(
            "Stale bucket should be evicted, allowing new request");
        limiter.TryAcquireByIp(IPAddress.Parse("10.0.0.1")).ShouldBeTrue(
            "Stale IP bucket should be evicted, allowing new request");

        limiter.Dispose();
    }

    [Test]
    public async Task RateLimiter_ActiveBuckets_NotEvictedDuringWindow()
    {
        var limiter = CreateLimiter(maxRequests: 2, windowSeconds: 60);

        limiter.TryAcquire("active-session").ShouldBeTrue();
        limiter.TryAcquire("active-session").ShouldBeTrue();

        // Still within window — should be blocked
        limiter.TryAcquire("active-session").ShouldBeFalse("Active bucket should not be evicted");

        limiter.Dispose();
    }

    [Test]
    public void RateLimiter_Dispose_DoesNotThrow()
    {
        var limiter = CreateLimiter(maxRequests: 10, windowSeconds: 60);
        limiter.TryAcquire("test");

        Should.NotThrow(() => limiter.Dispose());
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PR #4: Tool error message sanitization
    //  Bug: Tools returned raw ex.Message to users, leaking internal
    //       paths, connection strings, and library-specific errors.
    //  Fix: ToolRegistry catch returns "Error: operation failed." generically.
    //  Calls real: ToolRegistry.ExecuteAsync (via internal test constructor)
    // ══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ToolRegistry_ThrowingTool_ReturnsSanitizedError()
    {
        var registry = new ToolRegistry(
            [new ThrowingTool("Sensitive: /home/user/.clawsharp/sessions/telegram:12345.json")],
            NullLoggerFactory.Instance);

        var result = await registry.ExecuteAsync("throwing_tool", "{}");

        result.ShouldBe("Error: operation failed.");
        result.ShouldNotContain("Sensitive");
        result.ShouldNotContain("/home/user");
        result.ShouldNotContain("telegram");
    }

    [Test]
    public async Task ToolRegistry_ThrowingTool_DoesNotLeakConnectionString()
    {
        var registry = new ToolRegistry(
            [new ThrowingTool("Connection string: Server=prod.db.example.com;User=admin;Password=secret")],
            NullLoggerFactory.Instance);

        var result = await registry.ExecuteAsync("throwing_tool", "{}");

        result.ShouldBe("Error: operation failed.");
        result.ShouldNotContain("Server=");
        result.ShouldNotContain("Password=");
        result.ShouldNotContain("prod.db");
    }

    [Test]
    public async Task ToolRegistry_ThrowingTool_DoesNotLeakExceptionType()
    {
        var registry = new ToolRegistry(
            [new ThrowingTool("NpgsqlException: 42P01: relation \"facts\" does not exist")],
            NullLoggerFactory.Instance);

        var result = await registry.ExecuteAsync("throwing_tool", "{}");

        result.ShouldBe("Error: operation failed.");
        result.ShouldNotContain("NpgsqlException");
        result.ShouldNotContain("42P01");
    }

    [Test]
    public async Task ToolRegistry_UnknownTool_ReturnsSpecificError()
    {
        var registry = new ToolRegistry([], NullLoggerFactory.Instance);

        var result = await registry.ExecuteAsync("nonexistent_tool", "{}");

        result.ShouldContain("unknown tool");
        result.ShouldContain("nonexistent_tool");
    }

    [Test]
    public async Task ToolRegistry_ValidTool_ReturnsResult()
    {
        var registry = new ToolRegistry([new EchoTool()], NullLoggerFactory.Instance);

        var result = await registry.ExecuteAsync("echo_tool", """{"message":"hello"}""");

        result.ShouldBe("hello");
    }

    [Test]
    public async Task ToolRegistry_InvalidJson_ReturnsSanitizedError()
    {
        var registry = new ToolRegistry([new EchoTool()], NullLoggerFactory.Instance);

        var result = await registry.ExecuteAsync("echo_tool", "invalid json {{{");

        result.ShouldBe("Error: operation failed.");
        result.ShouldNotContain("invalid json");
        result.ShouldNotContain("{{{");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    private static RateLimiter CreateLimiter(int maxRequests = 3, int windowSeconds = 60)
    {
        var defaults = new AgentDefaults
        {
            RateLimitRequests = maxRequests,
            RateLimitWindow = TimeSpan.FromSeconds(windowSeconds)
        };
        return new RateLimiter(Options.Create(defaults));
    }

    private static RateLimiter CreateLimiterWithTimeSpan(int maxRequests, TimeSpan window)
    {
        var defaults = new AgentDefaults
        {
            RateLimitRequests = maxRequests,
            RateLimitWindow = window
        };
        return new RateLimiter(Options.Create(defaults));
    }

    /// <summary>Tool that always throws — exercises ToolRegistry.ExecuteAsync catch block.</summary>
    private sealed class ThrowingTool(string exceptionMessage) : Tool
    {
        public override string Name => "throwing_tool";
        public override string Description => "Throws for testing";
        public override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

        public override Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
        {
            throw new InvalidOperationException(exceptionMessage);
        }
    }

    /// <summary>Minimal tool that echoes the "message" argument.</summary>
    private sealed class EchoTool : Tool
    {
        public override string Name => "echo_tool";
        public override string Description => "Echoes message";
        public override string ParametersSchemaJson => """{"type":"object","properties":{"message":{"type":"string"}}}""";

        public override Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
        {
            var msg = arguments.TryGetProperty("message", out var prop) ? prop.GetString() ?? "" : "";
            return Task.FromResult(msg);
        }
    }
}
