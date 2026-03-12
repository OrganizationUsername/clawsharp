using Intellenum;

namespace Clawsharp.Core.Utilities;

/// <summary>Well-known channel name identifiers.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class ChannelName
{
    /// <summary>Command-line interface channel.</summary>
    public static readonly ChannelName Cli = new("cli");

    /// <summary>Telegram Bot API channel.</summary>
    public static readonly ChannelName Telegram = new("telegram");

    /// <summary>Discord gateway channel.</summary>
    public static readonly ChannelName Discord = new("discord");

    /// <summary>Slack RTM/socket channel.</summary>
    public static readonly ChannelName Slack = new("slack");

    /// <summary>Matrix client-server channel.</summary>
    public static readonly ChannelName Matrix = new("matrix");

    /// <summary>Email (IMAP/SMTP) channel.</summary>
    public static readonly ChannelName Email = new("email");

    /// <summary>IRC channel.</summary>
    public static readonly ChannelName Irc = new("irc");

    /// <summary>HTTP/WebSocket web channel.</summary>
    public static readonly ChannelName Web = new("web");

    /// <summary>Signal messenger channel (via signal-cli-rest-api bridge).</summary>
    public static readonly ChannelName Signal = new("signal");

    /// <summary>WhatsApp channel (via whatsapp-web.js bridge).</summary>
    public static readonly ChannelName WhatsApp = new("whatsapp");

    /// <summary>BlueBubbles iMessage channel (via BlueBubbles server).</summary>
    public static readonly ChannelName BlueBubbles = new("bluebubbles");

    /// <summary>LINE Messaging API channel.</summary>
    public static readonly ChannelName Line = new("line");

    /// <summary>WeChat Work (WeCom) robot channel.</summary>
    public static readonly ChannelName WeChat = new("wechat");

    /// <summary>Lark/Feishu Messaging API channel (webhook mode).</summary>
    public static readonly ChannelName Lark = new("lark");

    /// <summary>Mattermost channel (WebSocket + REST API v4).</summary>
    public static readonly ChannelName Mattermost = new("mattermost");

    /// <summary>Nostr protocol channel (NIP-04/NIP-17 encrypted DMs via relays).</summary>
    public static readonly ChannelName Nostr = new("nostr");

    /// <summary>NapCat/OneBot QQ messaging channel (WebSocket + REST API).</summary>
    public static readonly ChannelName Qq = new("qq");

    /// <summary>WeCom (WeChat Work) AI Bot webhook channel.</summary>
    public static readonly ChannelName WeCom = new("wecom");
}