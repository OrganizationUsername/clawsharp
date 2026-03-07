using System.Text.Json.Serialization;

namespace Clawsharp.Config.Channels;

/// <summary>Configuration for a messaging channel.</summary>
public sealed class ChannelConfig
{
    /// <summary>Whether this channel is enabled.</summary>
    public bool Enabled { get; set; }

    // Telegram
    /// <summary>Bot token (Telegram, Discord).</summary>
    public string? Token { get; set; }

    /// <summary>
    /// Telegram user allowlist. Entries may be usernames ("alice", "@alice"), numeric user IDs
    /// ("123456789"), or the wildcard "*" to allow everyone. Empty list = deny all.
    /// Null (not configured) = allow all (backward-compatible default).
    /// </summary>
    [JsonConverter(typeof(AllowListConverter))]
    public List<string>? AllowFrom { get; set; }

    // Discord
    // Token shared with above

    // Slack
    /// <summary>Slack bot token.</summary>
    public string? BotToken { get; set; }

    /// <summary>Slack app-level token for Socket Mode.</summary>
    public string? AppToken { get; set; }

    // Matrix
    /// <summary>Matrix homeserver URL.</summary>
    public string? Homeserver { get; set; }

    /// <summary>Matrix access token.</summary>
    public string? AccessToken { get; set; }

    /// <summary>List of allowed Matrix room IDs.</summary>
    public List<string>? AllowRooms { get; set; }

    // Email
    /// <summary>IMAP server hostname.</summary>
    public string? ImapHost { get; set; }

    /// <summary>IMAP server port (default 993).</summary>
    public int ImapPort { get; set; } = 993;

    /// <summary>SMTP server hostname.</summary>
    public string? SmtpHost { get; set; }

    /// <summary>SMTP server port (default 587).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Email username.</summary>
    public string? Username { get; set; }

    /// <summary>Email password.</summary>
    public string? Password { get; set; }

    // IRC
    /// <summary>IRC server hostname.</summary>
    public string? Host { get; set; }

    /// <summary>IRC server port (default 6697 for TLS).</summary>
    public int Port { get; set; } = 6697;

    /// <summary>IRC nickname.</summary>
    public string? Nick { get; set; }

    /// <summary>IRC channels to join.</summary>
    public List<string>? Channels { get; set; }

    /// <summary>NickServ password for IRC authentication.</summary>
    public string? NickServPassword { get; set; }

    // Shared access controls
    /// <summary>
    /// When true, the bot only responds in group contexts (channels, guilds, rooms) when explicitly
    /// mentioned (e.g. @bot, "botnick:", slash-command). Has no effect on private/DM messages.
    /// </summary>
    public bool RequireMention { get; set; }

    /// <summary>
    /// Allowlist of channel, room, or guild IDs the bot will respond in.
    /// Null = allow all. Empty list = deny all.
    /// Applies to: Slack (channel IDs), IRC (channel names), Discord (guild IDs via AllowedGuilds).
    /// </summary>
    public List<string>? AllowedChannels { get; set; }

    /// <summary>
    /// Email only: if set, the subject or first line of the message body must start with this
    /// prefix (e.g. "!ask") before the email is processed. Prevents newsletters and auto-replies
    /// from triggering the agent.
    /// </summary>
    public string? CommandPrefix { get; set; }

    // Web
    /// <summary>HTTP listen port for the web channel (default 3000).</summary>
    public int WebPort { get; set; } = 3000;

    /// <summary>HTTP listen host for the web channel (default localhost).</summary>
    public string WebHost { get; set; } = "localhost";

    /// <summary>Static pairing token for the web channel (null = dynamic pairing).</summary>
    public string? PairingToken { get; set; }

    /// <summary>
    /// When true, logs an advisory that a reverse proxy (nginx/Caddy) is required for TLS termination.
    /// HttpListener does not manage TLS directly.
    /// </summary>
    public bool Tls { get; set; }

    /// <summary>
    /// CORS allowed origins for the web channel.
    /// When null or empty, no CORS headers are emitted (fail-closed).
    /// Set to a list of allowed origins to enable cross-origin access to the Web channel.
    /// </summary>
    public string? AllowedOrigins { get; set; }

    /// <summary>
    /// DM policy for unknown senders.
    /// "open" = allow all, "allowlist" = use AllowFrom only, "pairing" = issue a 6-digit pairing code (default for new installs).
    /// </summary>
    public string? DmPolicy { get; set; } = "pairing";

    /// <summary>
    /// Discord guild (server) message policy.
    /// "mention" (default) = only respond when @mentioned or !-prefixed.
    /// "open" = respond to all messages in allowed guilds without requiring mention.
    /// </summary>
    public string? GroupPolicy { get; set; }

    // Signal / WhatsApp / BlueBubbles / WeChat (bridge URL for out-of-process bridges)
    /// <summary>Bridge HTTP URL (Signal: signal-cli-rest-api, WhatsApp: whatsapp-web.js bridge, BlueBubbles: server URL, WeChat: optional bridge).</summary>
    public string? BridgeUrl { get; set; }

    /// <summary>Registered phone number for Signal (e.g. "+12125551234").</summary>
    public string? PhoneNumber { get; set; }

    // LINE
    /// <summary>LINE channel secret for webhook signature verification.</summary>
    public string? Secret { get; set; }

    /// <summary>LINE webhook listener port (default 8080).</summary>
    public int LineWebhookPort { get; set; } = 8080;

    // WeChat Work (WeCom) — bridge mode
    /// <summary>WeCom robot webhook key for sending messages.</summary>
    public string? WebhookKey { get; set; }

    // WeCom AI Bot — native webhook mode
    /// <summary>WeCom AI Bot AES encoding key (43-character base64 string).</summary>
    public string? EncodingAesKey { get; set; }

    /// <summary>WeCom AI Bot webhook URL path (default: /wecom).</summary>
    public string? WebhookPath { get; set; }

    /// <summary>WeCom AI Bot webhook listener port (default: 7075).</summary>
    public int WeComWebhookPort { get; set; } = 7075;

    // Lark / Feishu
    /// <summary>Feishu/Lark App ID (format: cli_xxx).</summary>
    public string? AppId { get; set; }

    /// <summary>Feishu/Lark App Secret.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Feishu/Lark webhook verification token (used for signature verification in webhook mode).</summary>
    public string? VerificationToken { get; set; }

    /// <summary>
    /// Feishu API domain: "feishu" (default, open.feishu.cn) or "lark" (international, open.larksuite.com).
    /// </summary>
    public string? FeishuDomain { get; set; }

    /// <summary>Lark/Feishu webhook listener port (default 7074).</summary>
    public int LarkWebhookPort { get; set; } = 7074;

    // Mattermost
    /// <summary>Mattermost server URL (e.g. "https://mattermost.example.com").</summary>
    public string? MattermostUrl { get; set; }

    // Nostr
    /// <summary>Nostr private key in nsec1 (bech32) or 64-char hex format.</summary>
    public string? NostrPrivKey { get; set; }

    /// <summary>WebSocket relay URLs to connect to (e.g. "wss://relay.damus.io").</summary>
    public string[]? NostrRelays { get; set; }

    /// <summary>
    /// When true, send replies using NIP-17 gift-wrap (metadata-hiding, kind 1059).
    /// When false, use NIP-04 legacy encrypted DMs (kind 4, widely supported).
    /// Default: true.
    /// </summary>
    public bool NostrNip17 { get; set; } = true;

    // NapCat/OneBot QQ
    /// <summary>NapCat/OneBot WebSocket URL for receiving events (e.g. ws://127.0.0.1:3001/ws).</summary>
    public string? WebSocketUrl { get; set; }

    /// <summary>NapCat/OneBot REST API base URL for sending messages. Auto-derived from WebSocketUrl if not set.</summary>
    public string? ApiBaseUrl { get; set; }
}