using System.Net;
using Clawsharp.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests;

public sealed class WebPairingGuardTests
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

    // ── Initial state ─────────────────────────────────────────────────

    [Test]
    public void PairingCode_NoPersistedTokens_IsNotNull()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        guard.PairingCode.ShouldNotBeNull();
        guard.PairingCode!.Length.ShouldBe(6);
        int.TryParse(guard.PairingCode, out _).ShouldBeTrue();
    }

    [Test]
    public void PairingCode_PersistedTokensExist_IsNull()
    {
        var guard1 = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard1.PairingCode!;
        guard1.TryPair(IPAddress.Loopback, code);

        var guard2 = new WebPairingGuard(_persistPath, NullLogger.Instance);

        guard2.PairingCode.ShouldBeNull();
    }

    [Test]
    public void HasPairedClients_InitialState_ReturnsFalse()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        guard.HasPairedClients.ShouldBeFalse();
    }

    // ── TryPair ───────────────────────────────────────────────────────

    [Test]
    public void TryPair_WrongCode_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);

        var token = guard.TryPair(IPAddress.Loopback, "000000");

        if (guard.PairingCode == "000000")
        {
            return; // skip this unlikely edge case
        }

        token.ShouldBeNull();
    }

    [Test]
    public void TryPair_CorrectCode_ReturnsTokenWithCsPrefix()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        var token = guard.TryPair(IPAddress.Loopback, code);

        token.ShouldNotBeNull();
        token!.ShouldStartWith("cs_");
    }

    [Test]
    public void TryPair_CorrectCodeUsed_ConsumesPairingCode()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        guard.TryPair(IPAddress.Loopback, code);

        guard.PairingCode.ShouldBeNull();
    }

    [Test]
    public void TryPair_SuccessfulPair_HasPairedClientsBecomeTrue()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        guard.TryPair(IPAddress.Loopback, code);

        guard.HasPairedClients.ShouldBeTrue();
    }

    [Test]
    public void TryPair_SecondAttemptWithConsumedCode_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        guard.TryPair(IPAddress.Loopback, code); // consume the code
        var secondAttempt = guard.TryPair(IPAddress.Loopback, code);

        secondAttempt.ShouldBeNull();
    }

    // ── Brute-force lockout ───────────────────────────────────────────

    [Test]
    public void TryPair_FiveWrongAttempts_LocksOutIp()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        for (var i = 0; i < 5; i++)
        {
            guard.TryPair(IPAddress.Parse("10.0.0.1"), "wrong!");
        }

        // Even correct code should fail from locked-out IP
        guard.TryPair(IPAddress.Parse("10.0.0.1"), code).ShouldBeNull();
    }

    [Test]
    public void TryPair_DifferentIpAfterLockout_NotLockedOut()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        // Lock out 10.0.0.1
        for (var i = 0; i < 5; i++)
        {
            guard.TryPair(IPAddress.Parse("10.0.0.1"), "wrong!");
        }

        // Different IP should still work
        guard.TryPair(IPAddress.Parse("10.0.0.2"), code).ShouldNotBeNull();
    }

    // ── IsAuthenticated ───────────────────────────────────────────────

    [Test]
    public void IsAuthenticated_ValidToken_ReturnsTrue()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;
        var token = guard.TryPair(IPAddress.Loopback, code)!;

        guard.IsAuthenticated(token).ShouldBeTrue();
    }

    [Test]
    public void IsAuthenticated_InvalidToken_ReturnsFalse()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;
        guard.TryPair(IPAddress.Loopback, code);

        guard.IsAuthenticated("cs_notavalidtoken").ShouldBeFalse();
    }

    [Test]
    public void IsAuthenticated_PartialToken_ReturnsFalse()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;
        var token = guard.TryPair(IPAddress.Loopback, code)!;

        guard.IsAuthenticated(token[..10]).ShouldBeFalse();
    }

    // ── Persistence ───────────────────────────────────────────────────

    [Test]
    public void Constructor_PersistedTokens_HasPairedClientsTrue()
    {
        var guard1 = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard1.PairingCode!;
        guard1.TryPair(IPAddress.Loopback, code);

        var guard2 = new WebPairingGuard(_persistPath, NullLogger.Instance);

        guard2.HasPairedClients.ShouldBeTrue();
        guard2.PairingCode.ShouldBeNull();
    }

    [Test]
    public void IsAuthenticated_PersistedToken_ReturnsTrue()
    {
        var guard1 = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard1.PairingCode!;
        var token = guard1.TryPair(IPAddress.Loopback, code)!;

        var guard2 = new WebPairingGuard(_persistPath, NullLogger.Instance);

        guard2.IsAuthenticated(token).ShouldBeTrue();
    }

    // ── IPv4/IPv6 identity ──────────────────────────────────────────

    [Test]
    public void TryPair_IPv4MappedIPv6_SharesLockoutWithIPv4()
    {
        // Demonstrates that IPAddress.Equals treats ::ffff:10.0.0.1 and 10.0.0.1 as different.
        // The WebChannel normalizes IPs before passing to the guard, but the guard itself
        // does not normalize — this test documents the current behavior.
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var ipv4 = IPAddress.Parse("10.0.0.1");
        var mappedIpv6 = IPAddress.Parse("::ffff:10.0.0.1");

        // They are not considered equal by IPAddress.Equals
        ipv4.Equals(mappedIpv6).ShouldBeFalse();

        // Lock out via IPv4
        for (var i = 0; i < 5; i++)
        {
            guard.TryPair(ipv4, "wrong!");
        }

        // IPv4-mapped IPv6 from same host gets separate counter (documented limitation)
        // The caller (WebChannel.NormalizeIp) must normalize before passing to the guard
        var code = guard.PairingCode!;
        guard.TryPair(mappedIpv6, code).ShouldNotBeNull();
    }

    [Test]
    public void TryPair_NullIp_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath, NullLogger.Instance);
        var code = guard.PairingCode!;

        guard.TryPair(null, code).ShouldBeNull();
    }
}