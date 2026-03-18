namespace Clawsharp.Config.Channels;

/// <summary>Typed per-channel config snapshots extracted from the flat <see cref="ChannelConfig"/> DTO.</summary>
public static class ChannelConfigExtensions
{
    /// <summary>Typed config for the Telegram channel.</summary>
    public sealed record TelegramConfig(string Token, List<string>? AllowFrom);

    /// <summary>Typed config for the Discord channel.</summary>
    public sealed record DiscordConfig(string Token);

    /// <summary>Typed config for the Slack channel.</summary>
    public sealed record SlackConfig(string BotToken, string AppToken);

    /// <summary>Typed config for the Matrix channel.</summary>
    public sealed record MatrixConfig(string Homeserver, string AccessToken, List<string>? AllowRooms);

    /// <summary>Typed config for the Email channel.</summary>
    public sealed record EmailConfig(string ImapHost, int ImapPort, string SmtpHost, int SmtpPort, string Username, string Password);

    /// <summary>Typed config for the IRC channel.</summary>
    public sealed record IrcConfig(string Host, int Port, string Nick, List<string>? Channels, string? NickServPassword);

    /// <summary>Returns typed Telegram config, or null if the channel is disabled or misconfigured.</summary>
    public static TelegramConfig? GetTelegram(this ChannelConfig? cfg) =>
        cfg is { Enabled: true, Token: { } tok } ? new(tok, cfg.AllowFrom) : null;

    /// <summary>Returns typed Discord config, or null if the channel is disabled or misconfigured.</summary>
    public static DiscordConfig? GetDiscord(this ChannelConfig? cfg) =>
        cfg is { Enabled: true, Token: { } tok } ? new(tok) : null;

    /// <summary>Returns typed Slack config, or null if the channel is disabled or misconfigured.</summary>
    public static SlackConfig? GetSlack(this ChannelConfig? cfg) =>
        cfg is { Enabled: true, BotToken: { } bot, AppToken: { } app } ? new(bot, app) : null;

    /// <summary>Returns typed Matrix config, or null if the channel is disabled or misconfigured.</summary>
    public static MatrixConfig? GetMatrix(this ChannelConfig? cfg) =>
        cfg is { Enabled: true, AccessToken: { } tok } ? new(cfg.Homeserver ?? "", tok, cfg.AllowRooms) : null;

    /// <summary>Returns typed Email config, or null if the channel is disabled or misconfigured.</summary>
    public static EmailConfig? GetEmail(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true, ImapHost: { } imap, SmtpHost: { } smtp, Username: { } user, Password: { } pass })
        {
            return new(imap, cfg.ImapPort, smtp, cfg.SmtpPort, user, pass);
        }

        return null;
    }

    /// <summary>Returns typed IRC config, or null if the channel is disabled or misconfigured.</summary>
    public static IrcConfig? GetIrc(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true, Host: { } host, Nick: { } nick })
        {
            return new(host, cfg.Port, nick, cfg.Channels, cfg.NickServPassword);
        }

        return null;
    }

    /// <summary>Typed config for the Signal channel.</summary>
    public sealed record SignalConfig(string BridgeUrl, string PhoneNumber, List<string>? AllowFrom);

    /// <summary>Typed config for the WhatsApp channel.</summary>
    public sealed record WhatsAppConfig(string BridgeUrl, List<string>? AllowFrom);

    /// <summary>Typed config for the BlueBubbles channel.</summary>
    public sealed record BlueBubblesConfig(string BridgeUrl, string Password, List<string>? AllowFrom);

    /// <summary>Typed config for the LINE channel.</summary>
    public sealed record LineConfig(string Token, string Secret, int WebhookPort, List<string>? AllowFrom);

    /// <summary>Typed config for the WeChat channel.</summary>
    public sealed record WeChatConfig(string? BridgeUrl, string? WebhookKey, List<string>? AllowFrom);

    /// <summary>Returns typed Signal config, or null if the channel is disabled or misconfigured.</summary>
    public static SignalConfig? GetSignal(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true, BridgeUrl: { } url, PhoneNumber: { } phone })
        {
            return new(url, phone, cfg.AllowFrom);
        }

        return null;
    }

    /// <summary>Returns typed WhatsApp config, or null if the channel is disabled or misconfigured.</summary>
    public static WhatsAppConfig? GetWhatsApp(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true, BridgeUrl: { } url })
        {
            return new(url, cfg.AllowFrom);
        }

        return null;
    }

    /// <summary>Returns typed BlueBubbles config, or null if the channel is disabled or misconfigured.</summary>
    public static BlueBubblesConfig? GetBlueBubbles(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true, BridgeUrl: { } url, Password: { } pw })
        {
            return new(url, pw, cfg.AllowFrom);
        }

        return null;
    }

    /// <summary>Returns typed LINE config, or null if the channel is disabled or misconfigured.</summary>
    public static LineConfig? GetLine(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true, Token: { } tok, Secret: { } secret })
        {
            return new(tok, secret, cfg.LineWebhookPort, cfg.AllowFrom);
        }

        return null;
    }

    /// <summary>Returns typed WeChat config, or null if the channel is disabled or misconfigured.</summary>
    public static WeChatConfig? GetWeChat(this ChannelConfig? cfg)
    {
        if (cfg is { Enabled: true } && (cfg.BridgeUrl is not null || cfg.WebhookKey is not null))
        {
            return new(cfg.BridgeUrl, cfg.WebhookKey, cfg.AllowFrom);
        }

        return null;
    }
}