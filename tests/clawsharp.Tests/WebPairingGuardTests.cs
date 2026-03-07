using Clawsharp.Security;

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
        var guard = new WebPairingGuard(_persistPath);

        guard.PairingCode.ShouldNotBeNull();
        guard.PairingCode!.Length.ShouldBe(6);
        int.TryParse(guard.PairingCode, out _).ShouldBeTrue();
    }

    [Test]
    public void PairingCode_PersistedTokensExist_IsNull()
    {
        var guard1 = new WebPairingGuard(_persistPath);
        var code = guard1.PairingCode!;
        guard1.TryPair("127.0.0.1", code);

        var guard2 = new WebPairingGuard(_persistPath);

        guard2.PairingCode.ShouldBeNull();
    }

    [Test]
    public void HasPairedClients_InitialState_ReturnsFalse()
    {
        var guard = new WebPairingGuard(_persistPath);

        guard.HasPairedClients.ShouldBeFalse();
    }

    // ── TryPair ───────────────────────────────────────────────────────

    [Test]
    public void TryPair_WrongCode_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath);

        var token = guard.TryPair("127.0.0.1", "000000");

        if (guard.PairingCode == "000000")
        {
            return; // skip this unlikely edge case
        }

        token.ShouldBeNull();
    }

    [Test]
    public void TryPair_CorrectCode_ReturnsTokenWithCsPrefix()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;

        var token = guard.TryPair("127.0.0.1", code);

        token.ShouldNotBeNull();
        token!.ShouldStartWith("cs_");
    }

    [Test]
    public void TryPair_CorrectCodeUsed_ConsumesPairingCode()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;

        guard.TryPair("127.0.0.1", code);

        guard.PairingCode.ShouldBeNull();
    }

    [Test]
    public void TryPair_SuccessfulPair_HasPairedClientsBecomeTrue()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;

        guard.TryPair("127.0.0.1", code);

        guard.HasPairedClients.ShouldBeTrue();
    }

    [Test]
    public void TryPair_SecondAttemptWithConsumedCode_ReturnsNull()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;

        guard.TryPair("127.0.0.1", code); // consume the code
        var secondAttempt = guard.TryPair("127.0.0.1", code);

        secondAttempt.ShouldBeNull();
    }

    // ── Brute-force lockout ───────────────────────────────────────────

    [Test]
    public void TryPair_FiveWrongAttempts_LocksOutIp()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;

        for (var i = 0; i < 5; i++)
        {
            guard.TryPair("10.0.0.1", "wrong!");
        }

        // Even correct code should fail from locked-out IP
        guard.TryPair("10.0.0.1", code).ShouldBeNull();
    }

    [Test]
    public void TryPair_DifferentIpAfterLockout_NotLockedOut()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;

        // Lock out 10.0.0.1
        for (var i = 0; i < 5; i++)
        {
            guard.TryPair("10.0.0.1", "wrong!");
        }

        // Different IP should still work
        guard.TryPair("10.0.0.2", code).ShouldNotBeNull();
    }

    // ── IsAuthenticated ───────────────────────────────────────────────

    [Test]
    public void IsAuthenticated_ValidToken_ReturnsTrue()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;
        var token = guard.TryPair("127.0.0.1", code)!;

        guard.IsAuthenticated(token).ShouldBeTrue();
    }

    [Test]
    public void IsAuthenticated_InvalidToken_ReturnsFalse()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;
        guard.TryPair("127.0.0.1", code);

        guard.IsAuthenticated("cs_notavalidtoken").ShouldBeFalse();
    }

    [Test]
    public void IsAuthenticated_PartialToken_ReturnsFalse()
    {
        var guard = new WebPairingGuard(_persistPath);
        var code = guard.PairingCode!;
        var token = guard.TryPair("127.0.0.1", code)!;

        guard.IsAuthenticated(token[..10]).ShouldBeFalse();
    }

    // ── Persistence ───────────────────────────────────────────────────

    [Test]
    public void Constructor_PersistedTokens_HasPairedClientsTrue()
    {
        var guard1 = new WebPairingGuard(_persistPath);
        var code = guard1.PairingCode!;
        guard1.TryPair("127.0.0.1", code);

        var guard2 = new WebPairingGuard(_persistPath);

        guard2.HasPairedClients.ShouldBeTrue();
        guard2.PairingCode.ShouldBeNull();
    }

    [Test]
    public void IsAuthenticated_PersistedToken_ReturnsTrue()
    {
        var guard1 = new WebPairingGuard(_persistPath);
        var code = guard1.PairingCode!;
        var token = guard1.TryPair("127.0.0.1", code)!;

        var guard2 = new WebPairingGuard(_persistPath);

        guard2.IsAuthenticated(token).ShouldBeTrue();
    }
}