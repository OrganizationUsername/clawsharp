using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Clawsharp.Config.Channels;

namespace Clawsharp.Channels.Email;

public sealed partial class EmailChannel : LifecycleBackgroundService, IChannel
{
    private readonly IMessageBus _bus;

    private readonly ChannelConfig? _cfg;

    private readonly bool _enabled;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly string? _commandPrefix;

    private readonly ILogger<EmailChannel> _logger;

    private readonly HashSet<string> _seenUids = [];

    private readonly Queue<string> _seenUidsQueue = new();

    private static readonly string SeenUidsPath = Path.Combine(
        ConfigLoader.ExpandHome("~/.clawsharp"), "email_seen_uids.txt");

    /// <summary>Current delay for exponential backoff on IMAP errors (5s to 300s).</summary>
    private int _backoffDelayMs = 5_000;

    private const int MinBackoffMs = 5_000;

    private const int MaxBackoffMs = 300_000;

    public EmailChannel(IOptions<AppConfig> options, IMessageBus bus, ILogger<EmailChannel> logger, ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _approvedSenders = approvedSenders;
        _cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Email.Value);
        _enabled = _cfg is { Enabled: true };
        _allowPolicy = new AllowListPolicy(_cfg?.AllowFrom);

        _commandPrefix = _cfg?.CommandPrefix;
    }

    public ChannelName Name => ChannelName.Email;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        LoadSeenUids();
        LogStartingImapPollLoop(_logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollImapAsync(stoppingToken);
                // Reset backoff on successful poll
                _backoffDelayMs = MinBackoffMs;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogImapError(_logger, ex);
                await Task.Delay(_backoffDelayMs, stoppingToken);
                _backoffDelayMs = Math.Min(_backoffDelayMs * 2, MaxBackoffMs);
                continue;
            }

            await Task.Delay(30_000, stoppingToken);
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled || _cfg is null)
        {
            return;
        }

        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(_cfg.Username));
        email.To.Add(MailboxAddress.Parse(message.RecipientId));
        email.Subject = "Re: clawsharp";
        email.Body = new TextPart("plain") { Text = message.Text };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _cfg.SmtpHost ?? throw new InvalidOperationException("SMTP host required."),
            _cfg.SmtpPort,
            MailKit.Security.SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(_cfg.Username, _cfg.Password, ct);
        await client.SendAsync(email, ct);
        await client.DisconnectAsync(true, ct);
    }

    private async Task PollImapAsync(CancellationToken ct)
    {
        using var client = await ConnectImapClientAsync(ct);
        var inbox = client.Inbox;

        var uids = await inbox.SearchAsync(SearchQuery.NotSeen, ct);
        foreach (var uid in uids)
        {
            var uidStr = uid.ToString();
            if (!_seenUids.Add(uidStr))
            {
                continue;
            }

            _seenUidsQueue.Enqueue(uidStr);
            if (_seenUidsQueue.Count > 10_000)
            {
                var oldest = _seenUidsQueue.Dequeue();
                _seenUids.Remove(oldest);
            }

            var message = await inbox.GetMessageAsync(uid, ct);
            await ProcessMailMessageAsync(message, inbox, uid, ct);
        }

        await client.DisconnectAsync(true, ct);
    }

    /// <summary>Connects to the IMAP server, authenticates, and opens the inbox for reading.</summary>
    private async Task<ImapClient> ConnectImapClientAsync(CancellationToken ct)
    {
        var client = new ImapClient();
        await client.ConnectAsync(
            _cfg!.ImapHost ?? throw new InvalidOperationException("IMAP host required."),
            _cfg.ImapPort,
            MailKit.Security.SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_cfg.Username, _cfg.Password, ct);

        await client.Inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        return client;
    }

    /// <summary>Processes a single mail message: validates sender, extracts body, checks prefix, publishes, and marks seen.</summary>
    private async Task ProcessMailMessageAsync(
        MimeMessage message, IMailFolder inbox, UniqueId uid, CancellationToken ct)
    {
        // Use envelope sender (Sender header, else first From address) — harder to spoof than From
        var fromAddr = (message.Sender ?? message.From.Mailboxes.FirstOrDefault())?.Address ?? "unknown";
        var fromName = (message.Sender ?? message.From.Mailboxes.FirstOrDefault())?.Name ?? fromAddr;

        // Sender allowlist check (static AllowFrom + dynamic approved senders)
        if (!_allowPolicy.IsAllowed(fromAddr) && !await _approvedSenders.IsApprovedAsync(ChannelName.Email.Value, fromAddr))
        {
            LogBlockedSender(_logger, fromAddr);
            return;
        }

        var body = ExtractPlainTextBody(message);
        if (body is null)
        {
            return;
        }

        // Command prefix check — subject or first body line must start with prefix
        if (_commandPrefix is not null)
        {
            var subject = message.Subject ?? "";
            if (!subject.StartsWith(_commandPrefix, StringComparison.OrdinalIgnoreCase) &&
                !body.StartsWith(_commandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: fromAddr,
            SenderName: fromName,
            Text: $"[{message.Subject}]\n{body}",
            ThreadId: uid.ToString()
        ), ct);

        // Mark as read so the message is not reprocessed on next poll
        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        PersistSeenUids();
    }

    /// <summary>Extracts the plain text body, stripping quoted reply lines. Returns null if empty.</summary>
    private static string? ExtractPlainTextBody(MimeMessage message)
    {
        var rawBody = message.TextBody ?? "";
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        // Strip quoted reply lines (lines starting with ">")
        var bodyLines = rawBody.Split('\n')
                               .Where(l => !l.TrimStart().StartsWith('>'))
                               .ToArray();
        var body = string.Join('\n', bodyLines).Trim();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    /// <summary>Loads previously persisted seen UIDs from disk to survive restarts.</summary>
    private void LoadSeenUids()
    {
        try
        {
            if (!File.Exists(SeenUidsPath))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(SeenUidsPath))
            {
                if (!string.IsNullOrWhiteSpace(line) && _seenUids.Add(line))
                {
                    _seenUidsQueue.Enqueue(line);
                }
            }
        }
        catch (Exception ex)
        {
            LogSeenUidsLoadError(_logger, ex);
        }
    }

    /// <summary>Persists current seen UIDs to disk for restart resilience.</summary>
    private void PersistSeenUids()
    {
        try
        {
            var dir = Path.GetDirectoryName(SeenUidsPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            var tmpPath = SeenUidsPath + ".tmp";
            File.WriteAllLines(tmpPath, _seenUids);
            File.Move(tmpPath, SeenUidsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogSeenUidsPersistError(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting IMAP poll loop")]
    private static partial void LogStartingImapPollLoop(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "IMAP error")]
    private static partial void LogImapError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Blocked sender {FromAddr}")]
    private static partial void LogBlockedSender(ILogger logger, string fromAddr);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to load seen UIDs from disk")]
    private static partial void LogSeenUidsLoadError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Failed to persist seen UIDs to disk")]
    private static partial void LogSeenUidsPersistError(ILogger logger, Exception exception);
}