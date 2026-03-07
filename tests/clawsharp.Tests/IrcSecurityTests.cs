namespace Clawsharp.Tests;

/// <summary>
/// Tests for IRC channel security logic: nick allowlist, channel allowlist, directed-at-bot
/// detection, and mention cleanup.
/// Uses pattern replication approach from IrcChannel.
/// </summary>
public sealed class IrcSecurityTests
{
    // Replicates IrcChannel allowlist initialization logic
    private static (bool allowAll, HashSet<string>? allowed) BuildNickAllowlist(List<string>? allowFrom)
    {
        if (allowFrom is null)
        {
            return (true, null);
        }

        if (allowFrom.Count == 0)
        {
            return (false, null);
        }

        if (allowFrom.Contains("*"))
        {
            return (true, null);
        }

        return (false, new HashSet<string>(allowFrom, StringComparer.OrdinalIgnoreCase));
    }

    // Replicates IrcChannel channel allowlist initialization logic
    private static (bool allowAllChannels, HashSet<string>? allowedChannels) BuildChannelAllowlist(
        List<string>? allowedChannels)
    {
        if (allowedChannels is null)
        {
            return (true, null);
        }

        if (allowedChannels.Count == 0)
        {
            return (false, null);
        }

        return (false, new HashSet<string>(allowedChannels, StringComparer.OrdinalIgnoreCase));
    }

    // Replicates IrcChannel nick allowlist check from RunAsync
    private static bool IsNickAllowed(bool allowAll, HashSet<string>? allowed, string senderNick)
    {
        return allowAll || (allowed?.Contains(senderNick) ?? false);
    }

    // Replicates IrcChannel channel allowlist check from RunAsync
    // Private messages (target not starting with '#') bypass the channel filter
    private static bool IsChannelAllowed(bool allowAllChannels, HashSet<string>? allowedChannels,
                                         string target)
    {
        if (!target.StartsWith('#'))
        {
            return true; // PM bypasses channel filter
        }

        return allowAllChannels || (allowedChannels?.Contains(target) ?? false);
    }

    // Replicates IrcChannel directed-at-bot check from RunAsync
    private static bool IsDirectedAtBot(string nick, string target, string text)
    {
        // PM (target == nick) or mention in channel (text starts with "nick:" or "nick,")
        return target == nick || text.StartsWith(nick + ":") || text.StartsWith(nick + ",");
    }

    // Replicates IrcChannel mention cleanup from RunAsync
    private static string CleanMention(string nick, string text)
    {
        if (text.StartsWith(nick) && text.Length > nick.Length + 1)
        {
            return text[(nick.Length + 1)..].Trim();
        }

        return text;
    }

    // ── Nick allowlist tests ────────────────────────────────────────

    [Test]
    public void BuildNickAllowlist_NullList_AllowsAll()
    {
        var (allowAll, allowed) = BuildNickAllowlist(null);

        allowAll.ShouldBeTrue();
        IsNickAllowed(allowAll, allowed, "anyone").ShouldBeTrue();
    }

    [Test]
    public void IsNickAllowed_SpecificList_AllowsMatch()
    {
        var (allowAll, allowed) = BuildNickAllowlist(["alice", "bob"]);

        IsNickAllowed(allowAll, allowed, "alice").ShouldBeTrue();
        IsNickAllowed(allowAll, allowed, "bob").ShouldBeTrue();
    }

    [Test]
    public void IsNickAllowed_SpecificList_BlocksNonMatch()
    {
        var (allowAll, allowed) = BuildNickAllowlist(["alice", "bob"]);

        IsNickAllowed(allowAll, allowed, "eve").ShouldBeFalse();
    }

    [Test]
    public void BuildNickAllowlist_EmptyList_BlocksAll()
    {
        var (allowAll, allowed) = BuildNickAllowlist([]);

        allowAll.ShouldBeFalse();
        allowed.ShouldBeNull();
        IsNickAllowed(allowAll, allowed, "anyone").ShouldBeFalse();
    }

    // ── Channel allowlist tests ─────────────────────────────────────

    [Test]
    public void BuildChannelAllowlist_NullList_AllowsAll()
    {
        var (allowAllCh, allowedCh) = BuildChannelAllowlist(null);

        allowAllCh.ShouldBeTrue();
        IsChannelAllowed(allowAllCh, allowedCh, "#general").ShouldBeTrue();
    }

    [Test]
    public void IsChannelAllowed_SpecificList_AllowsMatchAndBlocksOthers()
    {
        var (allowAllCh, allowedCh) = BuildChannelAllowlist(["#general", "#dev"]);

        IsChannelAllowed(allowAllCh, allowedCh, "#general").ShouldBeTrue();
        IsChannelAllowed(allowAllCh, allowedCh, "#dev").ShouldBeTrue();
        IsChannelAllowed(allowAllCh, allowedCh, "#random").ShouldBeFalse();
    }

    [Test]
    public void IsChannelAllowed_PrivateMessage_BypassesChannelFilter()
    {
        // Private messages (target is a nick, not a channel) bypass channel filter
        var (allowAllCh, allowedCh) = BuildChannelAllowlist(["#general"]);

        // "alice" doesn't start with '#', so it's a PM — always allowed
        IsChannelAllowed(allowAllCh, allowedCh, "alice").ShouldBeTrue();
    }

    // ── Directed-at-bot detection tests ─────────────────────────────

    [Test]
    public void IsDirectedAtBot_ColonSuffix_ReturnsTrue()
    {
        IsDirectedAtBot("clawsharp", "#general", "clawsharp: hello").ShouldBeTrue();
    }

    [Test]
    public void IsDirectedAtBot_CommaSuffix_ReturnsTrue()
    {
        IsDirectedAtBot("clawsharp", "#general", "clawsharp, hello").ShouldBeTrue();
    }

    [Test]
    public void IsDirectedAtBot_DirectMessage_ReturnsTrue()
    {
        // PM: target equals the bot's nick
        IsDirectedAtBot("clawsharp", "clawsharp", "hello world").ShouldBeTrue();
    }

    [Test]
    public void IsDirectedAtBot_GroupMessageNoMention_ReturnsFalse()
    {
        IsDirectedAtBot("clawsharp", "#general", "hello world").ShouldBeFalse();
    }

    // ── Mention cleanup tests ───────────────────────────────────────

    [Test]
    public void CleanMention_BotPrefix_RemovesPrefixAndTrims()
    {
        CleanMention("clawsharp", "clawsharp: what is rust?").ShouldBe("what is rust?");
        CleanMention("clawsharp", "clawsharp, what is rust?").ShouldBe("what is rust?");
    }

    // ── Wildcard test ───────────────────────────────────────────────

    [Test]
    public void BuildNickAllowlist_Wildcard_AllowsAll()
    {
        var (allowAll, allowed) = BuildNickAllowlist(["*"]);

        allowAll.ShouldBeTrue();
        allowed.ShouldBeNull();
        IsNickAllowed(allowAll, allowed, "anyone").ShouldBeTrue();
    }
}