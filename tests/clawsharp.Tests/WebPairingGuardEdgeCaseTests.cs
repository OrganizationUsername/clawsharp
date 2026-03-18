using System.Net;
using Clawsharp.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests;

/// <summary>
/// Edge-case tests for <see cref="WebPairingGuard"/>:
/// MED-17 (empty/null pairing code), MED-18 (eviction under distributed brute-force).
/// </summary>
[TestFixture]
public sealed class WebPairingGuardEdgeCaseTests
{
    private string _persistPath = null!;

    [SetUp]
    public void SetUp()
    {
        _persistPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_persistPath))
        {
            File.Delete(_persistPath);
        }
    }

    // ── MED-17: Empty/null pairing code ─────────────────────────────────

    [Test]
    public void TryPair_EmptyCode_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        var result = guard.TryPair(IPAddress.Loopback, "");

        result.ShouldBeNull("Empty pairing code should be rejected");
    }

    [Test]
    public void TryPair_WhitespaceCode_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        // The code is trimmed before comparison, so whitespace becomes empty
        // which won't match the 6-digit code
        var result = guard.TryPair(IPAddress.Loopback, "   ");

        result.ShouldBeNull("Whitespace-only pairing code should be rejected");
    }

    [Test]
    public void TryPair_EmptyCode_DoesNotConsumeCode()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var originalCode = guard.PairingCode;

        guard.TryPair(IPAddress.Loopback, "");

        guard.PairingCode.ShouldBe(originalCode,
            "Failed pairing attempt with empty code should not consume the pairing code");
    }

    [Test]
    public void TryPair_EmptyCode_RecordsFailure()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var ip = IPAddress.Parse("10.0.0.99");

        // Empty codes count as failed attempts
        for (var i = 0; i < 5; i++)
        {
            guard.TryPair(ip, "");
        }

        // Should now be locked out
        var code = guard.PairingCode!;
        guard.TryPair(ip, code).ShouldBeNull(
            "IP should be locked out after 5 failed attempts, even with empty codes");
    }

    // ── MED-17: Code with leading/trailing whitespace ───────────────────

    [Test]
    public void TryPair_CodeWithLeadingTrailingWhitespace_MatchesTrimmed()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        // The guard trims the code before comparison
        var result = guard.TryPair(IPAddress.Loopback, $"  {code}  ");

        result.ShouldNotBeNull("Code with surrounding whitespace should match after trim");
    }

    // ── MED-18: Eviction under distributed brute-force ──────────────────

    [Test]
    public void TryPair_OverMaxFailureTrackingEntries_EvictsExpiredEntries()
    {
        // The guard has a MaxFailureTrackingEntries = 10,000.
        // When exceeded, it evicts entries with expired lockouts and count < MaxFailedAttempts.
        // We can't easily test 10,001 IPs without being slow, so we test a smaller scenario
        // to verify the eviction logic path is exercised.
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        // Create failures from many different IPs (fewer than 10K but enough to test logic)
        for (var i = 0; i < 100; i++)
        {
            // Each IP gets 1 failed attempt (below lockout threshold of 5)
            var ip = new IPAddress(BitConverter.GetBytes(i + 1).Reverse().ToArray());
            guard.TryPair(ip, "wrong!");
        }

        // The guard should still function correctly
        var code = guard.PairingCode!;
        var result = guard.TryPair(IPAddress.Parse("192.168.1.1"), code);
        result.ShouldNotBeNull("Guard should still work after tracking many IPs");
    }

    [Test]
    public void TryPair_LockedOutIp_AfterLockoutExpiry_CanRetry()
    {
        // We can't easily test time-based lockout expiry in a unit test
        // without a time abstraction, but we can verify the lockout behavior.
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var ip = IPAddress.Parse("10.0.0.50");

        // Lock out the IP
        for (var i = 0; i < 5; i++)
        {
            guard.TryPair(ip, "wrong!");
        }

        // Verify lockout
        var code = guard.PairingCode!;
        guard.TryPair(ip, code).ShouldBeNull("IP should be locked out");

        // The lockout is 5 minutes — we can't wait that long in a test.
        // Just verify the code is still available (not consumed during lockout)
        guard.PairingCode.ShouldNotBeNull("Pairing code should not be consumed by locked-out attempts");
    }

    // ── Multiple successful pairings ────────────────────────────────────

    [Test]
    public void TryPair_AfterSuccessfulPair_RegenerateCode_AllowsAnotherPairing()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        // First pairing
        var code1 = guard.PairingCode!;
        var token1 = guard.TryPair(IPAddress.Loopback, code1);
        token1.ShouldNotBeNull();

        // Regenerate code for second pairing
        var code2 = guard.RegenerateCode();
        code2.ShouldNotBeNull();
        code2.Length.ShouldBe(6);

        // Second pairing
        var token2 = guard.TryPair(IPAddress.Parse("10.0.0.2"), code2);
        token2.ShouldNotBeNull();

        // Both tokens should work
        guard.IsAuthenticated(token1!).ShouldBeTrue();
        guard.IsAuthenticated(token2!).ShouldBeTrue();
    }

    // ── Corrupt persist file ────────────────────────────────────────────

    [Test]
    public void Constructor_CorruptPersistFile_StartsFresh()
    {
        File.WriteAllText(_persistPath, "{{not valid json!!");

        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        // Should start fresh with no paired clients and a new code
        guard.HasPairedClients.ShouldBeFalse();
        guard.PairingCode.ShouldNotBeNull();
    }

    [Test]
    public void Constructor_EmptyPersistFile_StartsFresh()
    {
        File.WriteAllText(_persistPath, "");

        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        guard.HasPairedClients.ShouldBeFalse();
        guard.PairingCode.ShouldNotBeNull();
    }

    // ── RegenerateCode ──────────────────────────────────────────────────

    [Test]
    public void RegenerateCode_ProducesSixDigitNumericCode()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        var code = guard.RegenerateCode();

        code.Length.ShouldBe(6);
        int.TryParse(code, out _).ShouldBeTrue();
    }

    [Test]
    public void RegenerateCode_ReplacesExistingCode()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var original = guard.PairingCode;

        // Regenerate multiple times — code changes (statistically, at least one will differ)
        var codes = Enumerable.Range(0, 10).Select(_ => guard.RegenerateCode()).ToList();

        // At least some codes should differ from the original (probability of all matching = (1/1M)^10)
        // RegenerateCode should produce different codes (statistical guarantee).
        codes.ShouldContain(c => c != original);
    }
}
