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

namespace Clawsharp.Channels.Lark;

/// <summary>
/// Lark/Feishu Messaging channel. Receives messages via webhook (HttpListener)
/// and sends replies via the Feishu Open API.
/// </summary>
public sealed partial class LarkChannel : WebhookListenerBase, IChannel
{
    private readonly string _appId = "";

    private readonly string _appSecret = "";

    private readonly string _verificationToken = "";

    private readonly int _webhookPort;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _enabled;

    private readonly IMessageBus _bus;

    private readonly ILogger<LarkChannel> _logger;

    private readonly HttpClient _http;

    // Cached tenant access token with expiry
    private string? _tenantToken;

    private DateTimeOffset _tokenExpiry;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // Dedup: track recently processed event IDs to ignore retries
    private readonly BoundedDeduplicator<string> _dedup = new(1000, StringComparer.Ordinal);

    public ChannelName Name => ChannelName.Lark;

    public LarkChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<LarkChannel> logger,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _http = httpClientFactory.CreateClient("lark");
        _approvedSenders = approvedSenders;

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Lark.Value);
        if (cfg is not { Enabled: true } || cfg.AppId is null || cfg.AppSecret is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _appId = cfg.AppId;
        _appSecret = cfg.AppSecret;
        _verificationToken = cfg.VerificationToken ?? "";
        _webhookPort = cfg.LarkWebhookPort;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom);
    }

    // ── WebhookListenerBase overrides ────────────────────────────────────

    protected override bool IsEnabled => _enabled;

    protected override string GetListenerPrefix() => $"http://+:{_webhookPort}/";

    protected override void OnListenerStartFailed(HttpListenerException ex)
        => LogListenerStartFailed(ex, _webhookPort);

    protected override void OnListenerStarted()
    {
        if (_verificationToken.Length == 0)
        {
            LogNoVerificationToken();
        }

        LogStartingWebhook(_webhookPort);
    }

    protected override void OnRequestError(Exception ex) => LogWebhookError(ex);

    // ── Request handling ─────────────────────────────────────────────────

    protected override async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        // Only accept POST /lark/webhook
        if (req.HttpMethod != "POST" || !req.Url!.AbsolutePath.TrimEnd('/').Equals("/lark/webhook", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        // Read raw body
        using var ms = new MemoryStream();
        await req.InputStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bodyBytes = ms.ToArray();

        // Parse event envelope
        var webhookEvent = JsonSerializer.Deserialize(bodyBytes, LarkJsonContext.Default.LarkWebhookEvent);
        if (webhookEvent is null)
        {
            resp.StatusCode = 400;
            resp.Close();
            return;
        }

        // Handle url_verification challenge
        if (string.Equals(webhookEvent.Type, LarkWebhookType.UrlVerification, StringComparison.Ordinal))
        {
            if (_verificationToken.Length > 0 &&
                !string.Equals(webhookEvent.Token, _verificationToken, StringComparison.Ordinal))
            {
                resp.StatusCode = 403;
                resp.Close();
                return;
            }

            var challengeResp = JsonSerializer.SerializeToUtf8Bytes(
                new LarkChallengeResponse { Challenge = webhookEvent.Challenge ?? "" },
                LarkJsonContext.Default.LarkChallengeResponse);
            resp.ContentType = "application/json";
            resp.StatusCode = 200;
            await resp.OutputStream.WriteAsync(challengeResp, ct).ConfigureAwait(false);
            resp.Close();
            LogChallengeHandled();
            return;
        }

        // MED-45: Signature verification — REQUIRE valid signature when token is configured.
        // If no token is configured, we already logged a warning at startup (LogNoVerificationToken).
        if (_verificationToken.Length > 0)
        {
            var timestamp = req.Headers["X-Lark-Request-Timestamp"] ?? "";
            var nonce = req.Headers["X-Lark-Request-Nonce"] ?? "";
            var signature = req.Headers["X-Lark-Signature"];
            if (signature is null || !VerifySignature(timestamp, nonce, bodyBytes, signature))
            {
                LogInvalidSignature();
                resp.StatusCode = 403;
                resp.Close();
                return;
            }
        }

        // Handle im.message.receive_v1
        if (string.Equals(webhookEvent.Header?.EventType, LarkEventType.ImMessageReceiveV1, StringComparison.Ordinal))
        {
            await HandleMessageEventAsync(webhookEvent, ct).ConfigureAwait(false);
        }

        resp.StatusCode = 200;
        resp.Close();
    }

    private async Task HandleMessageEventAsync(LarkWebhookEvent webhookEvent, CancellationToken ct)
    {
        var evt = webhookEvent.Event;
        var msg = evt?.Message;
        if (msg is null)
        {
            return;
        }

        // Dedup by event_id (Feishu retries on non-200)
        var eventId = webhookEvent.Header?.EventId;
        if (eventId is not null && !_dedup.TryAdd(eventId))
        {
            return; // already processed
        }

        // Only handle text messages for now.
        // TODO: Lark's im.message.receive_v1 webhook does not include audio file binary data
        // inline. Audio messages have message_type "audio" but the content field contains only
        // a file_key reference. Downloading the audio requires calling the Lark File API
        // (GET /open-apis/im/v1/messages/{message_id}/resources/{file_key}?type=file)
        // with a tenant access token. Wire VoiceTranscriptionService here once the download
        // endpoint is implemented.
        if (!string.Equals(msg.MessageType, LarkMessageType.Text, StringComparison.Ordinal))
        {
            return;
        }

        // Extract sender open_id for allowlist + identity
        var openId = evt?.Sender?.SenderId?.OpenId;
        if (string.IsNullOrEmpty(openId))
        {
            return;
        }

        // Parse double-encoded content: content is a JSON string like "{\"text\":\"hello\"}"
        var text = ExtractTextContent(msg.Content);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // AllowFrom check
        if (!_allowPolicy.IsAllowed(openId) &&
            !await _approvedSenders.IsApprovedAsync(ChannelName.Lark.Value, openId).ConfigureAwait(false))
        {
            LogBlockedSender(openId);
            return;
        }

        // Use chat_id as SenderId (routes replies back to the right conversation)
        var chatId = msg.ChatId ?? openId;

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: chatId,
            SenderName: openId,
            Text: text
        ), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts text from the double-encoded content field.
    /// The content string is JSON like <c>{"text":"hello @_user_1"}</c>.
    /// </summary>
    private static string? ExtractTextContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        try
        {
            var inner = JsonSerializer.Deserialize(content, LarkJsonContext.Default.LarkTextContent);
            return inner?.Text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies the X-Lark-Signature header using HMAC-SHA256 of (timestamp + nonce + verificationToken + body).
    /// Uses constant-time comparison via <see cref="CryptographicOperations.FixedTimeEquals"/>.
    /// </summary>
    private bool VerifySignature(string timestamp, string nonce, byte[] body, string signature)
    {
        // Feishu signature: SHA256(timestamp + nonce + encrypt_key + body)
        // encrypt_key is the VerificationToken in plain-text webhook mode
        var payload = $"{timestamp}{nonce}{_verificationToken}{Encoding.UTF8.GetString(body)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexStringLower(hash);

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

        var token = await GetTenantTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
        {
            LogTokenRefreshFailed();
            return;
        }

        // Determine receive_id_type based on chat_id prefix
        var receiveIdType = message.RecipientId.StartsWith("oc_", StringComparison.Ordinal) ? "chat_id" : "open_id";

        var contentJson = JsonSerializer.Serialize(
            new LarkTextContent { Text = message.Text },
            LarkJsonContext.Default.LarkTextContent);

        var sendReq = new LarkSendMessageRequest
        {
            ReceiveId = message.RecipientId,
            Content = contentJson,
            MsgType = LarkMessageType.Text
        };

        var json = JsonSerializer.Serialize(sendReq, LarkJsonContext.Default.LarkSendMessageRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Post,
                $"open-apis/im/v1/messages?receive_id_type={receiveIdType}");
            httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            httpReq.Content = content;

            using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var responseBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                LogSendError(responseBody);
            }
        }
        catch (Exception ex)
        {
            LogSendFailed(ex);
        }
    }

    /// <summary>
    /// Gets or refreshes the tenant access token. Thread-safe via <see cref="_tokenLock"/>.
    /// Refreshes 5 minutes before expiry.
    /// </summary>
    private async Task<string?> GetTenantTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tenantToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                return _tenantToken;
            }

            var tokenReq = new LarkTokenRequest
            {
                AppId = _appId,
                AppSecret = _appSecret
            };

            var json = JsonSerializer.Serialize(tokenReq, LarkJsonContext.Default.LarkTokenRequest);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(
                "open-apis/auth/v3/tenant_access_token/internal/", content, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LogTokenHttpError(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var tokenResp = await JsonSerializer.DeserializeAsync(stream, LarkJsonContext.Default.LarkTokenResponse, ct)
                                                .ConfigureAwait(false);

            if (tokenResp is null || tokenResp.Code != 0 || string.IsNullOrEmpty(tokenResp.TenantAccessToken))
            {
                LogTokenApiError(tokenResp?.Code ?? -1, tokenResp?.Msg ?? "null response");
                return null;
            }

            _tenantToken = tokenResp.TenantAccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResp.Expire);
            LogTokenRefreshed(tokenResp.Expire);
            return _tenantToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ── LoggerMessage methods ────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting Lark webhook listener on port {Port}")]
    private partial void LogStartingWebhook(int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to start Lark webhook listener on port {Port}")]
    private partial void LogListenerStartFailed(Exception exception, int port);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Invalid X-Lark-Signature -- rejecting webhook")]
    private partial void LogInvalidSignature();

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Blocked Lark sender {OpenId}")]
    private partial void LogBlockedSender(string openId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Lark webhook handler error")]
    private partial void LogWebhookError(Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Lark send error: {ResponseBody}")]
    private partial void LogSendError(string responseBody);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Lark send failed")]
    private partial void LogSendFailed(Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "Lark url_verification challenge handled")]
    private partial void LogChallengeHandled();

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Lark tenant token refreshed (expires in {ExpireSeconds}s)")]
    private partial void LogTokenRefreshed(int expireSeconds);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "Lark tenant token refresh failed (no valid token)")]
    private partial void LogTokenRefreshFailed();

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Lark tenant token HTTP error: {ResponseBody}")]
    private partial void LogTokenHttpError(string responseBody);

    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "Lark tenant token API error: code={Code}, msg={Msg}")]
    private partial void LogTokenApiError(int code, string msg);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "Lark verification token is not configured -- webhook signature verification is DISABLED. " +
                  "All incoming requests will be accepted. Set verificationToken in the Lark channel config.")]
    private partial void LogNoVerificationToken();
}