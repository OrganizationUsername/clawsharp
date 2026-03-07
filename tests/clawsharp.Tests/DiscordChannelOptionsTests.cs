using Clawsharp.Channels.Discord;
using Clawsharp.Config;
using Clawsharp.Config.Channels;

namespace Clawsharp.Tests;

public sealed class DiscordChannelOptionsTests
{
    [Test]
    public void Constructor_AllowFromNull_AllowsAll()
    {
        var cfg = new ChannelConfig { AllowFrom = null };

        var options = new DiscordChannelOptions(cfg);

        options.AllowPolicy.IsAllowAll.ShouldBeTrue();
        options.AllowPolicy.IsAllowed("anyone").ShouldBeTrue();
    }

    [Test]
    public void Constructor_AllowFromEmpty_DeniesAll()
    {
        var cfg = new ChannelConfig { AllowFrom = [] };

        var options = new DiscordChannelOptions(cfg);

        options.AllowPolicy.IsAllowAll.ShouldBeFalse();
        options.AllowPolicy.IsAllowed("anyone").ShouldBeFalse();
    }

    [Test]
    public void Constructor_AllowFromWildcard_AllowsAll()
    {
        var cfg = new ChannelConfig { AllowFrom = ["*"] };

        var options = new DiscordChannelOptions(cfg);

        options.AllowPolicy.IsAllowAll.ShouldBeTrue();
        options.AllowPolicy.IsAllowed("anyone").ShouldBeTrue();
    }

    [Test]
    public void Constructor_AllowFromSpecificIds_PopulatesSet()
    {
        var cfg = new ChannelConfig { AllowFrom = ["123", "456"] };

        var options = new DiscordChannelOptions(cfg);

        options.AllowPolicy.IsAllowAll.ShouldBeFalse();
        options.AllowPolicy.IsAllowed("123").ShouldBeTrue();
        options.AllowPolicy.IsAllowed("456").ShouldBeTrue();
    }

    [Test]
    public void AllowPolicy_AllowAll_ReturnsTrue()
    {
        var cfg = new ChannelConfig { AllowFrom = null };
        var options = new DiscordChannelOptions(cfg);

        // AllowAll means any user is accepted
        options.AllowPolicy.IsAllowAll.ShouldBeTrue();
    }

    [Test]
    public void AllowPolicy_AllowedUser_ReturnsTrue()
    {
        var cfg = new ChannelConfig { AllowFrom = ["123", "456"] };
        var options = new DiscordChannelOptions(cfg);

        options.AllowPolicy.IsAllowed("123").ShouldBeTrue();
    }

    [Test]
    public void AllowPolicy_NotInSet_ReturnsFalse()
    {
        var cfg = new ChannelConfig { AllowFrom = ["123", "456"] };
        var options = new DiscordChannelOptions(cfg);

        options.AllowPolicy.IsAllowed("999").ShouldBeFalse();
    }

    [Test]
    public void Constructor_AllowedChannelsNull_NoGuildFilter()
    {
        var cfg = new ChannelConfig { AllowedChannels = null };

        var options = new DiscordChannelOptions(cfg);

        options.GuildAllowAll.ShouldBeTrue();
        options.GuildAllowList.ShouldBeNull();
    }

    [Test]
    public void Constructor_AllowedChannelsList_PopulatesGuildAllowList()
    {
        var cfg = new ChannelConfig { AllowedChannels = ["guild1", "guild2"] };

        var options = new DiscordChannelOptions(cfg);

        options.GuildAllowAll.ShouldBeFalse();
        options.GuildAllowList.ShouldNotBeNull();
        options.GuildAllowList!.Contains("guild1").ShouldBeTrue();
        options.GuildAllowList!.Contains("guild2").ShouldBeTrue();
    }
}