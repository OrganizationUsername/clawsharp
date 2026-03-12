using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Channels;

namespace Clawsharp.Channels.Irc;

public sealed partial class IrcChannel : LifecycleBackgroundService, IChannel
{
    private const int MaxChunkLen = 400;

    private const int InterChunkDelayMs = 500;

    private const int ReconnectBaseDelayMs = 5_000;

    private const int ReconnectMaxDelayMs = 60_000;

    private readonly IMessageBus _bus;

    private readonly ChannelConfig? _cfg;

    private readonly bool _enabled;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly AllowListPolicy _channelAllowPolicy = AllowListPolicy.AllowAll;

    private readonly ILogger<IrcChannel> _logger;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private TcpClient? _tcp;

    private Stream? _stream;

    private StreamWriter? _writer;

    public IrcChannel(IOptions<AppConfig> options, IMessageBus bus, ILogger<IrcChannel> logger, ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _approvedSenders = approvedSenders;
        _cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Irc.Value);
        _enabled = _cfg is { Enabled: true };
        _allowPolicy = new AllowListPolicy(_cfg?.AllowFrom);
        _channelAllowPolicy = new AllowListPolicy(_cfg?.AllowedChannels, StringComparer.OrdinalIgnoreCase);
    }

    public ChannelName Name => ChannelName.Irc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        var backoffMs = ReconnectBaseDelayMs;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
                backoffMs = ReconnectBaseDelayMs; // reset on clean exit
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogConnectionError(_logger, backoffMs, ex);
                await Task.Delay(backoffMs, stoppingToken);
                backoffMs = Math.Min(backoffMs * 2, ReconnectMaxDelayMs);
            }
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var target = message.RecipientId;
        // Split long messages (IRC line limit ~512 bytes)
        var text = message.Text;
        while (text.Length > 0)
        {
            var chunk = text.Length > MaxChunkLen ? text[..MaxChunkLen] : text;
            text = text.Length > MaxChunkLen ? text[MaxChunkLen..] : "";
            await SendRawAsync($"PRIVMSG {target} :{chunk}", ct);
            if (text.Length > 0)
            {
                await Task.Delay(InterChunkDelayMs, ct);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var host = _cfg!.Host ?? throw new InvalidOperationException("IRC host required.");
        var nick = _cfg!.Nick ?? "clawsharp";
        var channels = _cfg!.Channels ?? [];
        var useTls = _cfg.Tls || _cfg.Port is 6697 or 6698;

        var (reader, writer, nickServPassword) = await ConnectAsync(host, nick, useTls, ct);
        try
        {
            using var _ = reader;

            await RegisterAsync(writer, nick, nickServPassword, useTls, host, ct);

            LogConnected(_logger, host, _cfg.Port, nick);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                await HandleIrcLineAsync(line, nick, nickServPassword, channels, ct);
            }
        }
        finally
        {
            DisposeConnection();
        }
    }

    /// <summary>Opens the TCP connection and optional TLS stream, returning reader/writer.</summary>
    private async Task<(StreamReader Reader, StreamWriter Writer, string? NickServPassword)> ConnectAsync(
        string host, string nick, bool useTls, CancellationToken ct)
    {
        // Dispose any leftover connection from a previous attempt that failed before RunAsync's finally
        DisposeConnection();

        var tcp = new TcpClient();
        _tcp = tcp;
        await tcp.ConnectAsync(host, _cfg!.Port, ct);

        Stream stream;
        if (useTls)
        {
            var ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(host);
            stream = ssl;
        }
        else
        {
            stream = tcp.GetStream();
        }

        _stream = stream;
        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _writer = writer;
        var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        // Use a local copy so we never mutate the shared config object
        var nickServPassword = _cfg.NickServPassword;
        if (!string.IsNullOrEmpty(nickServPassword) && !useTls)
        {
            LogNickServNoTls(_logger, host, _cfg.Port);
            // Suppress IDENTIFY over plaintext — only clear the local variable
            nickServPassword = null;
        }

        return (reader, writer, nickServPassword);
    }

    /// <summary>Sends NICK, USER, and optionally NickServ IDENTIFY commands.</summary>
    private async Task RegisterAsync(
        StreamWriter writer, string nick, string? nickServPassword, bool useTls, string host,
        CancellationToken ct)
    {
        await writer.WriteLineAsync($"NICK {nick}".AsMemory(), ct);
        await writer.WriteLineAsync($"USER {nick} 0 * :clawsharp bot".AsMemory(), ct);
    }

    /// <summary>Dispatches a single IRC protocol line to the appropriate handler.</summary>
    private async Task HandleIrcLineAsync(
        string line, string nick, string? nickServPassword, List<string> channels,
        CancellationToken ct)
    {
        if (line.StartsWith("PING", StringComparison.Ordinal))
        {
            var token = line.Length > 5 ? line[5..] : "";
            await SendRawAsync($"PONG :{token}", ct);
            return;
        }

        // Parse: :nick!user@host COMMAND params :trailing
        if (line.Length == 0 || line[0] != ':')
        {
            return;
        }

        var parts = line.Split(' ', 4);
        if (parts.Length < 3)
        {
            return;
        }

        var command = parts[1];

        if (command == "001") // RPL_WELCOME — server accepted registration
        {
            // Identify with NickServ before joining channels
            if (!string.IsNullOrEmpty(nickServPassword))
            {
                await SendRawAsync($"PRIVMSG NickServ :IDENTIFY {nickServPassword}", ct);
            }

            foreach (var ch in channels)
            {
                await SendRawAsync($"JOIN {ch}", ct);
            }
        }

        if (command == "PRIVMSG" && parts.Length >= 4)
        {
            await HandlePrivmsgAsync(parts, nick, ct);
        }
    }

    /// <summary>Parses a PRIVMSG, checks allowlists, strips mentions, and publishes to the bus.</summary>
    private async Task HandlePrivmsgAsync(string[] parts, string nick, CancellationToken ct)
    {
        var prefix = parts[0][1..];
        var senderNick = prefix.Split('!')[0];
        var target = parts[2];
        var text = parts[3].TrimStart(':');

        // Nick allowlist check (static AllowFrom + dynamic approved senders)
        // Note: nicks are not authenticated without NickServ account tracking
        if (!_allowPolicy.IsAllowed(senderNick) &&
            !await _approvedSenders.IsApprovedAsync(ChannelName.Irc.Value, senderNick))
        {
            LogBlockedUser(_logger, senderNick);
            return;
        }

        // Channel allowlist check
        if (target.Length > 0 && target[0] == '#' && !_channelAllowPolicy.IsAllowed(target))
        {
            return;
        }

        // Only respond to messages directed at bot (PM or mention in channel)
        var hasNickPrefix = text.AsSpan().StartsWith(nick, StringComparison.Ordinal)
                            && text.Length > nick.Length
                            && text[nick.Length] is ':' or ',';
        if (target != nick && !hasNickPrefix)
        {
            return;
        }

        var cleanText = hasNickPrefix
            ? text[(nick.Length + 1)..].Trim()
            : text;
        if (string.IsNullOrWhiteSpace(cleanText))
        {
            return;
        }

        var replyTo = target == nick ? senderNick : target;

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: replyTo,
            SenderName: senderNick,
            Text: cleanText
        ), ct);
    }

    /// <summary>Disposes the TcpClient and its streams, nulling out fields so SendRawAsync becomes a no-op.</summary>
    private void DisposeConnection()
    {
        _writer = null;
        _stream = null;

        var tcp = _tcp;
        _tcp = null;
        tcp?.Dispose();
    }

    private async Task SendRawAsync(string line, CancellationToken ct)
    {
        // Exclude NickServ IDENTIFY from debug logging to avoid leaking credentials
        if (!line.StartsWith("PRIVMSG NickServ :IDENTIFY", StringComparison.OrdinalIgnoreCase))
        {
            LogOutgoingLine(_logger, line);
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            // Null check inside the lock to prevent TOCTOU race with reconnect path
            if (_writer is null)
            {
                return;
            }

            await _writer.WriteLineAsync(line.AsMemory(), ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Connection error, reconnecting in {BackoffMs}ms")]
    private static partial void LogConnectionError(ILogger logger, int backoffMs, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Connected to {Host}:{Port} as {Nick}")]
    private static partial void LogConnected(ILogger logger, string host, int port, string nick);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Blocked nick {Nick}")]
    private static partial void LogBlockedUser(ILogger logger, string nick);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "NickServ password will NOT be sent without TLS to {Host}:{Port}. " +
                  "Set port to 6697/6698 or enable tls: true in IRC channel config.")]
    private static partial void LogNickServNoTls(ILogger logger, string host, int port);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "IRC >> {Line}")]
    private static partial void LogOutgoingLine(ILogger logger, string line);
}