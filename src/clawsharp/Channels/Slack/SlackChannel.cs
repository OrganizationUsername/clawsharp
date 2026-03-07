using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawsharp.Channels;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Slack;

public sealed partial class SlackChannel : LifecycleBackgroundService, IChannel, IStreamingChannel, IFileChannel, IThinkingIndicator
{
    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly AllowListPolicy _channelAllowPolicy = AllowListPolicy.AllowAll;

    private readonly bool _requireMention;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly string _appToken = "";

    private readonly string _botToken = "";

    private readonly IMessageBus _bus;

    private readonly string? _dmPolicy;

    private readonly bool _enabled;

    private readonly HttpClient _http;

    private readonly ILogger<SlackChannel> _logger;

    private readonly PairingStore _pairingStore;

    private volatile string? _selfId;

    public SlackChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<SlackChannel> logger,
        PairingStore pairingStore,
        ApprovedSendersStore approvedSenders,
        IHttpClientFactory httpClientFactory)
    {
        _bus = bus;
        _logger = logger;
        _pairingStore = pairingStore;
        _approvedSenders = approvedSenders;
        _http = httpClientFactory.CreateClient("slack");

        var config = options.Value;
        var cfg = config.Channels.GetValueOrDefault(ChannelName.Slack.Value);
        if (cfg is not { Enabled: true } || cfg.BotToken is null || cfg.AppToken is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _botToken = cfg.BotToken;
        _appToken = cfg.AppToken;
        _dmPolicy = cfg.DmPolicy;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom, StringComparer.Ordinal);
        _channelAllowPolicy = new AllowListPolicy(cfg.AllowedChannels);
        _requireMention = cfg.RequireMention;
    }

    public ChannelName Name => ChannelName.Slack;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        LogStartingSocketMode(_logger);
        await FetchSelfIdAsync(stoppingToken);
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSocketModeAsync(stoppingToken);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, consecutiveFailures), 60));
                LogSocketModeError(_logger, ex);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        // Slack API rejects empty text — send single space as minimum content
        var text = string.IsNullOrWhiteSpace(message.Text) ? " " : ConvertToMrkdwn(message.Text);

        var resp = await ExecuteAsync(new SlackPostMessageRequest
        {
            Channel = message.RecipientId,
            Text = text,
            ThreadTs = message.ThreadId,
            Url = "chat.postMessage"
        }, ct);

        if (resp is not null && !resp.Ok)
        {
            LogSendFailed(_logger, "ok=false");
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendFileAsync(string recipientId, string filename, ReadOnlyMemory<byte> content,
                                          string? message, string? threadId, CancellationToken ct)
    {
        if (!_enabled)
        {
            return false;
        }

        return await UploadFileAsync(recipientId, filename, content, message, ct);
    }

    /// <inheritdoc />
    public Task StartThinkingAsync(string recipientId, CancellationToken ct = default)
    {
        // Slack bots cannot display a native typing indicator via the Socket Mode / Web API.
        // A reactions-based workaround would require the original message timestamp, which
        // is not reliably available at this call site. No-op for now.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopThinkingAsync(string recipientId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Serializes and dispatches an <see cref="IRequest{TResponse}"/> via HTTP POST,
    /// then deserializes the response body.
    /// </summary>
    private async Task<TResponse?> ExecuteAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, request.RequestTypeInfo);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, request.Url);
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(httpReq, ct);
        if (!resp.IsSuccessStatusCode)
        {
            LogSendFailed(_logger, await resp.Content.ReadAsStringAsync(ct));
            return default;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, request.ResponseTypeInfo, ct);
    }

    /// <inheritdoc />
    public async Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var result = await ThrottledStreamWriter.WriteWithResultAsync(
            tokens,
            sendInitialAsync: async (cursor, c) =>
            {
                // Slack API rejects empty text — cursor char is always non-empty but guard defensively
                var cursorText = string.IsNullOrWhiteSpace(cursor) ? " " : cursor;
                var postResp = await ExecuteAsync(new SlackPostMessageRequest
                {
                    Channel = message.RecipientId,
                    Text = cursorText,
                    ThreadTs = message.ThreadId,
                    Url = "chat.postMessage"
                }, c).ConfigureAwait(false);
                return postResp?.Ok == true ? postResp.Ts : null;
            },
            editMessageAsync: async (ts, text, c) =>
            {
                // Convert accumulated text to Slack mrkdwn; guard empty
                var mrkdwn = string.IsNullOrWhiteSpace(text) ? " " : ConvertToMrkdwn(text);
                await ExecuteAsync(new SlackUpdateMessageRequest
                {
                    Channel = message.RecipientId,
                    Ts = ts,
                    Text = mrkdwn
                }, c).ConfigureAwait(false);
            },
            ct: ct).ConfigureAwait(false);

        // If the placeholder failed, send as a new message.
        if (!result.PlaceholderCreated)
        {
            await SendAsync(message with { Text = result.Text }, ct).ConfigureAwait(false);
        }
    }

    private async Task FetchSelfIdAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "auth.test");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
            using var resp = await _http.SendAsync(req, ct);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            if (doc.RootElement.TryGetProperty("user_id", out var uid))
            {
                _selfId = uid.GetString();
                LogBotUserId(_logger, _selfId);
            }
        }
        catch (Exception ex)
        {
            LogFetchBotUserIdFailed(_logger, ex);
        }
    }

    private async Task RunSocketModeAsync(CancellationToken ct)
    {
        // Get WSS URL via apps.connections.open
        using var connReq = new HttpRequestMessage(HttpMethod.Post, "apps.connections.open");
        connReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _appToken);
        using var connResp = await _http.SendAsync(connReq, ct);
        await using var connStream = await connResp.Content.ReadAsStreamAsync(ct);
        using var connDoc = await JsonDocument.ParseAsync(connStream, default, ct);
        if (!connDoc.RootElement.TryGetProperty("url", out var urlProp))
        {
            throw new InvalidOperationException("apps.connections.open failed: url property not found in response");
        }

        var wssUrl = urlProp.GetString()!;

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wssUrl), ct);
        LogSocketModeConnected(_logger);

        await foreach (var message in WebSocketReceiver.ReceiveMessagesAsync(ws, MaxWebSocketMessageBytes, ct).ConfigureAwait(false))
        {
            SlackSocketEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize(message, SlackJsonContext.Default.SlackSocketEnvelope);
            }
            catch (JsonException)
            {
                continue;
            }

            if (envelope is null)
            {
                continue;
            }

            // Ack immediately
            if (envelope.EnvelopeId is not null)
            {
                var ack = JsonSerializer.Serialize(new SlackAcknowledgeResponse { EnvelopeId = envelope.EnvelopeId },
                    SlackJsonContext.Default.SlackAcknowledgeResponse);
                await ws.SendAsync(Encoding.UTF8.GetBytes(ack), WebSocketMessageType.Text, true, ct);
            }

            if (envelope.Type == "events_api")
            {
                await HandleEventAsync(envelope.Payload, ct);
            }
        }

        await WebSocketReceiver.CloseGracefullyAsync(ws);
    }

    private async Task HandleEventAsync(JsonElement payload, CancellationToken ct)
    {
        var fields = ExtractEventFields(payload);
        if (fields is null)
        {
            return;
        }

        var (text, userId, channelId, ts, threadTs) = fields.Value;

        if (!await CheckUserAllowedAsync(userId, channelId, payload.GetProperty("event"), ct))
        {
            return;
        }

        // Channel allowlist check
        if (!_channelAllowPolicy.IsAllowed(channelId))
        {
            return;
        }

        if (!StripMentionAndValidate(ref text, channelId))
        {
            return;
        }

        // Thread isolation: when a message is in a thread (has thread_ts), include the
        // thread timestamp in the SenderId so each thread gets its own session context.
        // The ThreadId is set to the thread parent timestamp so replies stay in the thread.
        // For top-level messages (no thread_ts), use the message's own ts as ThreadId.
        var senderId = threadTs is not null ? $"{channelId}:thread_{threadTs}" : channelId;
        var replyThreadTs = threadTs ?? ts;

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: senderId,
            SenderName: userId,
            Text: text,
            ThreadId: replyThreadTs
        ), ct);
    }

    /// <summary>
    /// Parses the basic fields from a Slack event payload. Validates that the event
    /// is a "message" type with no bot_id or subtype, and that text is non-empty.
    /// Returns null if the event should be skipped.
    /// </summary>
    private static (string Text, string UserId, string ChannelId, string? Ts, string? ThreadTs)? ExtractEventFields(JsonElement payload)
    {
        if (!payload.TryGetProperty("event", out var ev))
        {
            return null;
        }

        if (!ev.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "message")
        {
            return null;
        }

        if (ev.TryGetProperty("bot_id", out _))
        {
            return null; // ignore bot messages
        }

        if (ev.TryGetProperty("subtype", out _))
        {
            return null; // ignore subtypes
        }

        var text = ev.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var userId = ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
        var channelId = ev.TryGetProperty("channel", out var ch) ? ch.GetString() ?? "" : "";
        var ts = ev.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : null;
        var threadTs = ev.TryGetProperty("thread_ts", out var threadTsProp) ? threadTsProp.GetString() : null;

        return (text, userId, channelId, ts, threadTs);
    }

    /// <summary>
    /// Checks whether the user is allowed to interact with the bot. If the user is not
    /// allowed and the message is a DM with pairing policy, sends a pairing code.
    /// Returns true if the user is allowed, false if blocked.
    /// </summary>
    private async Task<bool> CheckUserAllowedAsync(string userId, string channelId, JsonElement ev, CancellationToken ct)
    {
        var isAllowed = _allowPolicy.IsAllowed(userId)
                        || await _approvedSenders.IsApprovedAsync("slack", userId);
        if (isAllowed)
        {
            return true;
        }

        // DM pairing flow: if policy is "pairing" and this is a DM, send a pairing code.
        var isDmForPairing = channelId.StartsWith('D');
        if (isDmForPairing && string.Equals(_dmPolicy, "pairing", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var userName = ev.TryGetProperty("user_profile", out var up) && up.TryGetProperty("display_name", out var dn)
                    ? dn.GetString() ?? userId
                    : userId;
                var code = await _pairingStore.GetOrCreateCodeAsync("slack", userId, userName, ct);
                await PostPairingMessageAsync(userId, code, ct);
                LogPairingSent(_logger, userId, code);
            }
            catch (Exception ex)
            {
                LogPairingFailed(_logger, ex);
            }
        }
        else
        {
            LogBlockedUser(_logger, userId);
        }

        return false;
    }

    /// <summary>
    /// Handles mention filtering for non-DM channels and strips the bot mention from the text.
    /// Returns true if the text is valid and should be processed, false to skip.
    /// </summary>
    private bool StripMentionAndValidate(ref string text, string channelId)
    {
        // Require mention in non-DM channels (channel IDs starting with 'C' are public/private channels; 'D' = DM)
        var isDm = channelId.StartsWith('D');
        if (_requireMention && !isDm)
        {
            if (_selfId is null || !text.Contains($"<@{_selfId}>", StringComparison.Ordinal))
            {
                return false;
            }
        }

        // Strip bot mention
        if (_selfId is not null)
        {
            text = text.Replace($"<@{_selfId}>", "", StringComparison.Ordinal).Trim();
        }

        return !string.IsNullOrWhiteSpace(text);
    }

    private const int MaxWebSocketMessageBytes = 1 * 1024 * 1024; // 1 MB

    /// <summary>
    /// Converts standard Markdown to Slack mrkdwn format.
    /// Slack uses different formatting: **bold** → *bold*, __italic__ → _italic_,
    /// ~~strike~~ → ~strike~, [text](url) → &lt;url|text&gt;, ### heading → *heading*.
    /// Code blocks and inline code are left unchanged.
    /// </summary>
    internal static string ConvertToMrkdwn(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown;
        }

        // Extract code blocks and inline code to protect them from conversion
        var codeBlocks = new List<string>();
        var result = CodeBlockRegex().Replace(markdown, m =>
        {
            codeBlocks.Add(m.Value);
            return $"\x00CB{codeBlocks.Count - 1}\x00";
        });
        var inlineCode = new List<string>();
        result = InlineCodeRegex().Replace(result, m =>
        {
            inlineCode.Add(m.Value);
            return $"\x00IC{inlineCode.Count - 1}\x00";
        });

        // 1. Bold: **text** → *text* (must run before italic to avoid conflict)
        result = BoldRegex().Replace(result, "*$1*");

        // 2. Italic: __text__ → _text_ (double-underscore variant)
        result = ItalicDoubleUnderscoreRegex().Replace(result, "_$1_");
        // Single-underscore _text_ stays as _text_ (Slack already uses this for italic)

        // 3. Strikethrough: ~~text~~ → ~text~
        result = StrikethroughRegex().Replace(result, "~$1~");

        // 4. Links: [text](url) → <url|text>
        result = LinkRegex().Replace(result, "<$2|$1>");

        // 5. Headers: ### heading → *heading* (Slack has no header markup; bold is the closest)
        result = HeaderRegex().Replace(result, "*$1*");

        // 6. Unordered lists: lines starting with - or * followed by space → bullet
        result = UnorderedListDashRegex().Replace(result, "\u2022 ");
        result = UnorderedListAsteriskRegex().Replace(result, "\u2022 ");

        // Restore inline code and code blocks in a single pass each (avoids N intermediate string allocations)
        if (inlineCode.Count > 0)
        {
            result = InlineCodeSentinelRegex().Replace(result, m => inlineCode[int.Parse(m.Groups[1].ValueSpan)]);
        }

        if (codeBlocks.Count > 0)
        {
            result = CodeBlockSentinelRegex().Replace(result, m => codeBlocks[int.Parse(m.Groups[1].ValueSpan)]);
        }

        return result;
    }

    // --- GeneratedRegex for AOT-compatible mrkdwn conversion ---

    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"`[^`]+`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"__(.+?)__")]
    private static partial Regex ItalicDoubleUnderscoreRegex();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^- ", RegexOptions.Multiline)]
    private static partial Regex UnorderedListDashRegex();

    [GeneratedRegex(@"^\* ", RegexOptions.Multiline)]
    private static partial Regex UnorderedListAsteriskRegex();

    [GeneratedRegex(@"\u00CB(\d+)\x00")]
    private static partial Regex CodeBlockSentinelRegex();

    [GeneratedRegex(@"\x00IC(\d+)\x00")]
    private static partial Regex InlineCodeSentinelRegex();

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting Socket Mode")]
    private static partial void LogStartingSocketMode(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Socket Mode error, reconnecting with backoff")]
    private static partial void LogSocketModeError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Send failed: {ResponseBody}")]
    private static partial void LogSendFailed(ILogger logger, string responseBody);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Bot user ID: {BotUserId}")]
    private static partial void LogBotUserId(ILogger logger, string? botUserId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Failed to fetch bot user ID")]
    private static partial void LogFetchBotUserIdFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Socket Mode connected")]
    private static partial void LogSocketModeConnected(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Blocked user {UserId}")]
    private static partial void LogBlockedUser(ILogger logger, string userId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "Failed to post streaming draft")]
    private static partial void LogDraftPostFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "Sent pairing code to Slack user {UserId}: {Code}")]
    private static partial void LogPairingSent(ILogger logger, string userId, string code);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Failed to send Slack pairing code")]
    private static partial void LogPairingFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "File upload step {Step} failed for {Filename}: {Error}")]
    private static partial void LogFileUploadStepFailed(ILogger logger, int step, string filename, string error);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information,
        Message = "Uploaded file {Filename} ({Length} bytes) to channel {ChannelId}")]
    private static partial void LogFileUploaded(ILogger logger, string filename, int length, string channelId);

    /// <summary>
    /// Uploads a file to a Slack channel using the modern 3-step file upload API:
    /// <list type="number">
    ///   <item>POST <c>files.getUploadURLExternal</c> to get an upload URL and file ID.</item>
    ///   <item>PUT the file bytes to the upload URL.</item>
    ///   <item>POST <c>files.completeUploadExternal</c> to finalize and share the file.</item>
    /// </list>
    /// </summary>
    /// <param name="channelId">Slack channel ID to share the file in.</param>
    /// <param name="filename">Display filename (e.g. "report.pdf").</param>
    /// <param name="content">Raw file bytes.</param>
    /// <param name="initialComment">Optional message to accompany the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the upload completed successfully.</returns>
    internal async Task<bool> UploadFileAsync(string channelId, string filename, ReadOnlyMemory<byte> content, string? initialComment,
                                              CancellationToken ct)
    {
        // Step 1: Get upload URL
        using var step1Req = new HttpRequestMessage(HttpMethod.Post,
            $"files.getUploadURLExternal?filename={Uri.EscapeDataString(filename)}&length={content.Length}");
        step1Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);

        using var step1Resp = await _http.SendAsync(step1Req, ct);
        if (!step1Resp.IsSuccessStatusCode)
        {
            LogFileUploadStepFailed(_logger, 1, filename, $"HTTP {(int)step1Resp.StatusCode}");
            return false;
        }

        await using var step1Stream = await step1Resp.Content.ReadAsStreamAsync(ct);
        var uploadUrl = await JsonSerializer.DeserializeAsync(step1Stream, SlackJsonContext.Default.SlackUploadUrlResponse, ct);
        if (uploadUrl is not { Ok: true } || uploadUrl.UploadUrl is null || uploadUrl.FileId is null)
        {
            LogFileUploadStepFailed(_logger, 1, filename, uploadUrl?.Error ?? "missing upload_url or file_id");
            return false;
        }

        // Step 2: PUT file bytes to the upload URL
        using var step2Req = new HttpRequestMessage(HttpMethod.Put, uploadUrl.UploadUrl);
        step2Req.Content = new ReadOnlyMemoryContent(content);
        step2Req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        using var step2Resp = await _http.SendAsync(step2Req, ct);
        if (!step2Resp.IsSuccessStatusCode)
        {
            LogFileUploadStepFailed(_logger, 2, filename, $"HTTP {(int)step2Resp.StatusCode}");
            return false;
        }

        // Step 3: Complete the upload and share to channel
        var completeResp = await ExecuteAsync(new SlackCompleteUploadRequest
        {
            Files = [new SlackFileReference { Id = uploadUrl.FileId, Title = filename }],
            ChannelId = channelId,
            InitialComment = initialComment
        }, ct);

        if (completeResp is not { Ok: true })
        {
            LogFileUploadStepFailed(_logger, 3, filename, completeResp?.Error ?? "ok=false");
            return false;
        }

        LogFileUploaded(_logger, filename, content.Length, channelId);
        return true;
    }

    /// <summary>Sends a DM pairing code to a Slack user via chat.postMessage.</summary>
    private async Task PostPairingMessageAsync(string userId, string code, CancellationToken ct)
    {
        var msg = $"Hi! To use this bot, send your operator the pairing code: *{code}*\n" +
                  "This code expires in 24 hours.";

        await ExecuteAsync(new SlackPostMessageRequest
        {
            Channel = userId,
            Text = msg,
            Url = "chat.postMessage"
        }, ct);
    }
}