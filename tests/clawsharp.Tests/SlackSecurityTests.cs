namespace Clawsharp.Tests;

/// <summary>
/// Tests for Slack channel security logic (allowlist, channel filter, mention requirement).
/// Uses pattern replication: the allowlist/mention/channel filtering logic from SlackChannel
/// is replicated as local static methods, tested directly without needing the full SlackChannel
/// constructor dependency tree.
/// </summary>
public sealed class SlackSecurityTests
{
    // Replicates SlackChannel allowlist initialization logic
    private static (bool allowAll, HashSet<string>? allowed) BuildAllowlist(List<string>? allowFrom)
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

        return (false, new HashSet<string>(allowFrom.Select(id => id.ToString())));
    }

    // Replicates SlackChannel channel allowlist logic
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

        return (false, new HashSet<string>(allowedChannels));
    }

    // Replicates SlackChannel.HandleEventAsync user allowlist check
    private static bool IsUserAllowed(bool allowAll, HashSet<string>? allowed, string userId)
    {
        return allowAll || (allowed?.Contains(userId) ?? false);
    }

    // Replicates SlackChannel.HandleEventAsync channel allowlist check
    private static bool IsChannelAllowed(bool allowAllChannels, HashSet<string>? allowedChannels,
                                         string channelId)
    {
        return allowAllChannels || (allowedChannels?.Contains(channelId) ?? false);
    }

    // Replicates SlackChannel.HandleEventAsync require-mention check
    private static bool PassesMentionCheck(bool requireMention, string channelId, string? selfId,
                                           string text)
    {
        var isDm = channelId.StartsWith('D');
        if (requireMention && !isDm)
        {
            if (selfId is null || !text.Contains($"<@{selfId}>", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // Replicates SlackChannel.HandleEventAsync bot_id check
    private static bool IsBotMessage(bool hasBotId) => hasBotId;

    [Test]
    public void BuildAllowlist_NullList_AllowsAll()
    {
        var (allowAll, allowed) = BuildAllowlist(null);

        allowAll.ShouldBeTrue();
        allowed.ShouldBeNull();
        IsUserAllowed(allowAll, allowed, "U12345").ShouldBeTrue();
    }

    [Test]
    public void IsUserAllowed_SpecificList_BlocksNonMembers()
    {
        var (allowAll, allowed) = BuildAllowlist(["U111", "U222"]);

        allowAll.ShouldBeFalse();
        IsUserAllowed(allowAll, allowed, "U111").ShouldBeTrue();
        IsUserAllowed(allowAll, allowed, "U999").ShouldBeFalse();
    }

    [Test]
    public void BuildAllowlist_EmptyList_BlocksAll()
    {
        var (allowAll, allowed) = BuildAllowlist([]);

        allowAll.ShouldBeFalse();
        allowed.ShouldBeNull();
        IsUserAllowed(allowAll, allowed, "U12345").ShouldBeFalse();
    }

    [Test]
    public void IsChannelAllowed_NonAllowedChannel_ReturnsFalse()
    {
        var (allowAllCh, allowedCh) = BuildChannelAllowlist(["C111", "C222"]);

        IsChannelAllowed(allowAllCh, allowedCh, "C111").ShouldBeTrue();
        IsChannelAllowed(allowAllCh, allowedCh, "C999").ShouldBeFalse();
    }

    [Test]
    public void IsChannelAllowed_AllowedChannel_ReturnsTrue()
    {
        var (allowAllCh, allowedCh) = BuildChannelAllowlist(["C111"]);

        IsChannelAllowed(allowAllCh, allowedCh, "C111").ShouldBeTrue();
    }

    [Test]
    public void PassesMentionCheck_GroupWithoutMention_ReturnsFalse()
    {
        // Channel starting with 'C' is a group channel
        var result = PassesMentionCheck(
            requireMention: true,
            channelId: "C12345",
            selfId: "U_BOT",
            text: "hello world");

        result.ShouldBeFalse();
    }

    [Test]
    public void PassesMentionCheck_GroupWithMention_ReturnsTrue()
    {
        var result = PassesMentionCheck(
            requireMention: true,
            channelId: "C12345",
            selfId: "U_BOT",
            text: "hey <@U_BOT> what's up?");

        result.ShouldBeTrue();
    }

    [Test]
    public void PassesMentionCheck_DirectMessage_BypassesMentionCheck()
    {
        // DMs have channelId starting with 'D' — mention check is bypassed
        var result = PassesMentionCheck(
            requireMention: true,
            channelId: "D12345",
            selfId: "U_BOT",
            text: "hello without mention");

        result.ShouldBeTrue();
    }

    [Test]
    public void IsBotMessage_BotIdPresent_ReturnsTrue()
    {
        IsBotMessage(hasBotId: true).ShouldBeTrue();
        IsBotMessage(hasBotId: false).ShouldBeFalse();
    }

    [Test]
    public void BuildAllowlist_Wildcard_AllowsAll()
    {
        var (allowAll, allowed) = BuildAllowlist(["*"]);

        allowAll.ShouldBeTrue();
        allowed.ShouldBeNull();
        IsUserAllowed(allowAll, allowed, "U_ANY_USER").ShouldBeTrue();
    }
}