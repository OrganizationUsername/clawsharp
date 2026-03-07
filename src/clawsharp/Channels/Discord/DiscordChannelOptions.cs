using Clawsharp.Config;
using Clawsharp.Config.Channels;

namespace Clawsharp.Channels.Discord;

/// <summary>Config extracted from ChannelConfig for Discord-specific DI injection.</summary>
public sealed class DiscordChannelOptions
{
    public DiscordChannelOptions(ChannelConfig cfg)
    {
        AllowPolicy = new AllowListPolicy(cfg.AllowFrom, StringComparer.Ordinal);

        if (cfg.AllowedChannels is null)
        {
            GuildAllowAll = true;
        }
        else if (cfg.AllowedChannels.Count == 0)
        {
            GuildAllowAll = false;
        }
        else
        {
            GuildAllowList = new HashSet<string>(cfg.AllowedChannels);
        }

        DmPolicy = cfg.DmPolicy;
        GroupPolicy = cfg.GroupPolicy;
    }

    /// <summary>User allowlist policy built from <c>AllowFrom</c>.</summary>
    public AllowListPolicy AllowPolicy { get; }

    /// <summary>True when AllowedChannels is null — all guilds accepted.</summary>
    public bool GuildAllowAll { get; }

    /// <summary>Allowlisted Discord guild (server) IDs. Null when GuildAllowAll is true or when empty-list denies all.</summary>
    public IReadOnlySet<string>? GuildAllowList { get; }

    /// <summary>
    ///     DM policy for unknown senders: "open", "allowlist", or "pairing".
    ///     When "pairing", unknown DM senders receive a 6-digit code to share with the operator.
    /// </summary>
    public string? DmPolicy { get; }

    /// <summary>
    ///     Guild message policy: "mention" (default) or "open".
    ///     When "open", the bot responds to all messages in allowed guilds without requiring @mention or !-prefix.
    /// </summary>
    public string? GroupPolicy { get; }
}