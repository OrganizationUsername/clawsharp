using System.Reflection;
using Clawsharp.Channels.Telegram;

namespace Clawsharp.Tests;

/// <summary>
/// Tests for Telegram channel allowlist logic: Normalize and IsUserAllowed.
/// Uses reflection to invoke the private static Normalize method directly.
/// IsUserAllowed tests use pattern replication to avoid constructing TelegramChannel
/// (which requires redirecting an initonly static field in ApprovedSendersStore).
/// The replicated logic mirrors TelegramChannel's constructor + IsUserAllowed exactly.
/// </summary>
public sealed class TelegramAllowlistTests
{
    // ── Normalize tests (private static method via reflection) ──────

    private static string InvokeNormalize(string entry)
    {
        var method = typeof(TelegramChannel).GetMethod("Normalize",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [entry])!;
    }

    [Test]
    public void Normalize_AtPrefix_RemovesPrefix()
    {
        InvokeNormalize("@alice").ShouldBe("alice");
    }

    [Test]
    public void Normalize_Whitespace_TrimsWhitespace()
    {
        InvokeNormalize("  bob  ").ShouldBe("bob");
    }

    [Test]
    public void Normalize_MixedCase_PreservesCase()
    {
        // Normalize does NOT lowercase — it only trims @ and whitespace.
        // The OrdinalIgnoreCase HashSet handles case insensitivity.
        var result = InvokeNormalize("Alice");
        result.ShouldBe("Alice");
    }

    [Test]
    public void Normalize_AlreadyClean_ReturnsUnchanged()
    {
        InvokeNormalize("alice").ShouldBe("alice");
    }

    [Test]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        InvokeNormalize("").ShouldBe("");
    }

    // ── IsUserAllowed tests (pattern replication) ───────────────────

    // Replicates TelegramChannel.Normalize
    private static string Normalize(string entry) => entry.TrimStart('@').Trim();

    // Replicates TelegramChannel constructor allowlist initialization
    private static (bool allowAll, bool wildcard, HashSet<string>? allowed) BuildAllowlist(
        List<string>? allowFrom)
    {
        if (allowFrom is null)
        {
            return (true, false, null);
        }

        if (allowFrom.Count == 0)
        {
            return (false, false, null);
        }

        if (allowFrom.Contains("*"))
        {
            return (false, true, null);
        }

        return (false, false,
            new HashSet<string>(allowFrom.Select(Normalize), StringComparer.OrdinalIgnoreCase));
    }

    // Replicates TelegramChannel.IsUserAllowed logic (without ApprovedSendersStore check)
    private static bool IsUserAllowed(bool allowAll, bool wildcard, HashSet<string>? allowed,
                                      long userId, string? username)
    {
        if (allowAll || wildcard)
        {
            return true;
        }

        if (allowed is null)
        {
            return false; // empty list -> deny all
        }

        if (allowed.Contains(userId.ToString()))
        {
            return true;
        }

        if (username is { Length: > 0 } name && allowed.Contains(Normalize(name)))
        {
            return true;
        }

        return false;
    }

    [Test]
    public void IsUserAllowed_NullAllowFrom_AllowsAll()
    {
        var (allowAll, wildcard, allowed) = BuildAllowlist(null);
        IsUserAllowed(allowAll, wildcard, allowed, 12345, "alice").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_EmptyList_DeniesAll()
    {
        var (allowAll, wildcard, allowed) = BuildAllowlist([]);
        IsUserAllowed(allowAll, wildcard, allowed, 12345, "alice").ShouldBeFalse();
    }

    [Test]
    public void IsUserAllowed_Wildcard_AllowsAll()
    {
        var (allowAll, wildcard, allowed) = BuildAllowlist(["*"]);
        IsUserAllowed(allowAll, wildcard, allowed, 12345, "alice").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_ByNumericId_Allowed()
    {
        var (allowAll, wildcard, allowed) = BuildAllowlist(["12345"]);
        IsUserAllowed(allowAll, wildcard, allowed, 12345, "alice").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_ByUsername_Allowed()
    {
        var (allowAll, wildcard, allowed) = BuildAllowlist(["alice"]);
        IsUserAllowed(allowAll, wildcard, allowed, 99999, "alice").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_CaseInsensitiveHashSet_MatchesAnyCase()
    {
        // The HashSet uses OrdinalIgnoreCase, so "Alice" matches "alice"
        var (allowAll, wildcard, allowed) = BuildAllowlist(["Alice"]);
        IsUserAllowed(allowAll, wildcard, allowed, 99999, "alice").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_AtPrefixInList_MatchesNormalized()
    {
        // "@alice" in AllowFrom gets normalized to "alice" via Normalize
        var (allowAll, wildcard, allowed) = BuildAllowlist(["@alice"]);
        IsUserAllowed(allowAll, wildcard, allowed, 99999, "alice").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_NullUsername_FallsBackToNumericId()
    {
        // User has no username, only a numeric ID
        var (allowAll1, wildcard1, allowed1) = BuildAllowlist(["12345"]);
        IsUserAllowed(allowAll1, wildcard1, allowed1, 12345, username: null).ShouldBeTrue();

        var (allowAll2, wildcard2, allowed2) = BuildAllowlist(["alice"]);
        IsUserAllowed(allowAll2, wildcard2, allowed2, 99999, username: null).ShouldBeFalse();
    }

    [Test]
    public void IsUserAllowed_MultipleEntries_MatchesAllFormats()
    {
        var list = new List<string> { "alice", "12345", "@bob" };
        var (allowAll, wildcard, allowed) = BuildAllowlist(list);

        IsUserAllowed(allowAll, wildcard, allowed, 99999, "alice").ShouldBeTrue();
        IsUserAllowed(allowAll, wildcard, allowed, 12345, "charlie").ShouldBeTrue();
        IsUserAllowed(allowAll, wildcard, allowed, 99999, "bob").ShouldBeTrue();
        IsUserAllowed(allowAll, wildcard, allowed, 88888, "unknown").ShouldBeFalse();
    }
}