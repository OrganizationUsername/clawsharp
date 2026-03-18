using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Line;

/// <summary>
/// LINE Messaging API channel. Receives messages via webhook (HttpListener)
/// and sends replies via the LINE push message API.
/// </summary>
public sealed partial class LineChannel : WebhookListenerBase, IChannel
{
    private readonly string _token = "";

    private readonly byte[] _secretBytes = [];

    private readonly int _webhookPort;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _enabled;

    private readonly IMessageBus _bus;

    private readonly ILogger<LineChannel> _logger;

    private readonly HttpClient _http;

    public ChannelName Name => ChannelName.Line;

    public LineChannel(IOptions<AppConfig> options, IMessageBus bus, ILogger<LineChannel> logger, IHttpClientFactory httpClientFactory,
                       ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _http = httpClientFactory.CreateClient("line");
        _approvedSenders = approvedSenders;

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Line.Value);
        if (cfg is not { Enabled: true } || cfg.Token is null || cfg.Secret is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _token = cfg.Token;
        _secretBytes = Encoding.UTF8.GetBytes(cfg.Secret);
        _webhookPort = cfg.LineWebhookPort;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom);
    }

    // ── WebhookListenerBase overrides ────────────────────────────────────

    protected override bool IsEnabled => _enabled;

    protected override string GetListenerPrefix() => $"http://+:{_webhookPort}/";

    protected override void OnListenerStartFailed(HttpListenerException ex)
        => LogListenerStartFailed(_logger, _webhookPort, ex);

    protected override void OnListenerStarted()
        => LogStartingWebhook(_logger, _webhookPort);

    protected override void OnRequestError(Exception ex) => LogWebhookError(_logger, ex);

    // ── Request handling ─────────────────────────────────────────────────

    protected override async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        // Only accept POST /webhook
        if (req.HttpMethod != "POST" || !req.Url!.AbsolutePath.TrimEnd('/').Equals("/webhook", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        var bodyBytes = await ReadAndVerifyBodyAsync(req, ct).ConfigureAwait(false);
        if (bodyBytes is null)
        {
            resp.StatusCode = 403;
            resp.Close();
            return;
        }

        // Parse webhook body
        var webhookReq = JsonSerializer.Deserialize(bodyBytes, LineJsonContext.Default.LineWebhookRequest);
        if (webhookReq?.Events is null)
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        foreach (var evt in webhookReq.Events)
        {
            await ProcessMessageEventAsync(evt, ct).ConfigureAwait(false);
        }

        resp.StatusCode = 200;
        resp.Close();
    }

    /// <summary>
    /// Reads the request body and verifies the X-Line-Signature header.
    /// Returns the raw body bytes if valid, or <c>null</c> if the signature is missing or invalid.
    /// </summary>
    private async Task<byte[]?> ReadAndVerifyBodyAsync(HttpListenerRequest req, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await req.InputStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bodyBytes = ms.ToArray();

        var signature = req.Headers["X-Line-Signature"];
        if (signature is null || !VerifySignature(bodyBytes, signature))
        {
            LogInvalidSignature(_logger);
            return null;
        }

        return bodyBytes;
    }

    /// <summary>
    /// Processes a single LINE webhook event: filters non-message/non-text events,
    /// checks the sender allowlist, and publishes to the message bus.
    /// </summary>
    private async Task ProcessMessageEventAsync(LineEvent evt, CancellationToken ct)
    {
        if (!string.Equals(evt.Type, LineEventType.Message, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(evt.Message?.Type, LineMessageType.Text, StringComparison.OrdinalIgnoreCase))
        {
            // LINE supports audio messages (type "audio") with a content endpoint at
            // GET https://api-data.line.me/v2/bot/message/{messageId}/content.
            // However, downloading audio requires an authenticated HTTP call and
            // the message ID from evt.Message.Id.
            // TODO: Wire VoiceTranscriptionService here once the LINE content download
            // endpoint is implemented. Reference: TelegramChannel voice handling pattern.
            return;
        }

        var userId = evt.Source?.UserId;
        var text = evt.Message?.Text;
        if (userId is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Static AllowFrom + dynamic approved senders
        if (!_allowPolicy.IsAllowed(userId) &&
            !await _approvedSenders.IsApprovedAsync(ChannelName.Line.Value, userId).ConfigureAwait(false))
        {
            LogBlockedSender(_logger, userId);
            return;
        }

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: userId,
            SenderName: userId,
            Text: text
        ), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the X-Line-Signature header using HMAC-SHA256 of the request body
    /// with the channel secret, compared using constant-time equality.
    /// </summary>
    private bool VerifySignature(byte[] body, string signature)
    {
        using var hmac = new HMACSHA256(_secretBytes);
        var hash = hmac.ComputeHash(body);
        var expected = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        // Use LINE push API (reply tokens expire quickly, push is more reliable)
        var req = new LinePushRequest
        {
            To = message.RecipientId,
            Messages = [new LineTextMessage { Text = message.Text }]
        };

        var json = JsonSerializer.Serialize(req, LineJsonContext.Default.LinePushRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "v2/bot/message/push");
            httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            httpReq.Content = content;

            using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                LogSendError(_logger, body);
            }
        }
        catch (Exception ex)
        {
            LogSendFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting LINE webhook listener on port {Port}")]
    private static partial void LogStartingWebhook(ILogger logger, int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to start LINE webhook listener on port {Port}")]
    private static partial void LogListenerStartFailed(ILogger logger, int port, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Invalid X-Line-Signature — rejecting webhook")]
    private static partial void LogInvalidSignature(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Blocked sender {UserId}")]
    private static partial void LogBlockedSender(ILogger logger, string userId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Webhook handler error")]
    private static partial void LogWebhookError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Send error: {ResponseBody}")]
    private static partial void LogSendError(ILogger logger, string responseBody);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Send failed")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}