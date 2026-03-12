using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.WeCom;

/// <summary>
/// WeCom (WeChat Work) AI Bot webhook channel.
/// Receives messages via HTTP webhook with AES-256-CBC encryption
/// and replies by POSTing to the per-message <c>response_url</c>.
/// </summary>
internal sealed partial class WeComChannel : WebhookListenerBase, IChannel
{
    private readonly string _token = "";

    private readonly byte[] _aesKey = [];

    private readonly string _webhookPath = "/wecom";

    private readonly int _webhookPort;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _enabled;

    private readonly IMessageBus _bus;

    private readonly ILogger<WeComChannel> _logger;

    private readonly HttpClient _http;

    /// <summary>Maps recipientId to the most recent response_url for sending replies.</summary>
    private readonly ConcurrentDictionary<string, string> _responseUrls = new(StringComparer.Ordinal);

    /// <summary>Deduplicates incoming messages by msgid (capped at 1000).</summary>
    private readonly BoundedDeduplicator<string> _dedup = new(1000, StringComparer.Ordinal);

    public ChannelName Name => ChannelName.WeCom;

    public WeComChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<WeComChannel> logger,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _http = httpClientFactory.CreateClient("wecom");
        _approvedSenders = approvedSenders;

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.WeCom.Value);
        if (cfg is not { Enabled: true } || cfg.Token is null || cfg.EncodingAesKey is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _token = cfg.Token;

        try
        {
            _aesKey = WeComCrypto.DeriveAesKey(cfg.EncodingAesKey);
        }
        catch (Exception ex)
        {
            LogInvalidAesKey(ex);
            _enabled = false;
            return;
        }

        _webhookPath = (cfg.WebhookPath ?? "/wecom").TrimEnd('/');
        _webhookPort = cfg.WeComWebhookPort;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom);
    }

    // ── WebhookListenerBase overrides ────────────────────────────────────

    protected override bool IsEnabled => _enabled;

    protected override string GetListenerPrefix() => $"http://+:{_webhookPort}/";

    protected override void OnListenerStartFailed(HttpListenerException ex)
        => LogListenerStartFailed(ex, _webhookPort);

    protected override void OnListenerStarted()
        => LogStartingWebhook(_webhookPort);

    protected override void OnRequestError(Exception ex) => LogWebhookError(ex);

    // ── Request handling ─────────────────────────────────────────────────

    protected override async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        var path = req.Url!.AbsolutePath.TrimEnd('/');

        if (!path.Equals(_webhookPath, StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        // Extract query parameters shared by both GET and POST
        var msgSignature = req.QueryString["msg_signature"] ?? "";
        var timestamp = req.QueryString["timestamp"] ?? "";
        var nonce = req.QueryString["nonce"] ?? "";

        if (req.HttpMethod == "GET")
        {
            await HandleVerificationAsync(resp, msgSignature, timestamp, nonce,
                req.QueryString["echostr"] ?? "", ct).ConfigureAwait(false);
            return;
        }

        if (req.HttpMethod == "POST")
        {
            await HandleMessageAsync(resp, msgSignature, timestamp, nonce, req.InputStream, ct).ConfigureAwait(false);
            return;
        }

        resp.StatusCode = 405;
        resp.Close();
    }

    /// <summary>
    /// Handles the WeCom webhook URL verification (GET).
    /// Verifies the signature, decrypts the echostr, and returns the plaintext.
    /// </summary>
    private async Task HandleVerificationAsync(
        HttpListenerResponse resp, string msgSignature, string timestamp,
        string nonce, string echostr, CancellationToken ct)
    {
        if (!WeComCrypto.VerifySignature(_token, timestamp, nonce, echostr, msgSignature))
        {
            LogInvalidSignature();
            resp.StatusCode = 403;
            resp.Close();
            return;
        }

        try
        {
            var plaintext = WeComCrypto.Decrypt(_aesKey, echostr);
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            resp.StatusCode = 200;
            resp.ContentType = "text/plain";
            await resp.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            resp.Close();
            LogVerificationHandled();
        }
        catch (Exception ex)
        {
            LogDecryptionFailed(ex);
            resp.StatusCode = 403;
            resp.Close();
        }
    }

    /// <summary>
    /// Handles an incoming WeCom AI Bot message (POST).
    /// Parses XML, verifies signature, decrypts JSON, extracts text, and publishes to the bus.
    /// </summary>
    private async Task HandleMessageAsync(
        HttpListenerResponse resp, string msgSignature, string timestamp,
        string nonce, Stream body, CancellationToken ct)
    {
        var xmlResult = await ReadAndParseXmlBodyAsync(body, ct).ConfigureAwait(false);
        if (xmlResult is null)
        {
            resp.StatusCode = 400;
            resp.Close();
            return;
        }

        var encryptContent = xmlResult.Value.Encrypt;

        // Verify signature
        if (!WeComCrypto.VerifySignature(_token, timestamp, nonce, encryptContent, msgSignature))
        {
            LogInvalidSignature();
            resp.StatusCode = 403;
            resp.Close();
            return;
        }

        // Respond HTTP 200 immediately (WeCom expects a fast response)
        var okBytes = "success"u8.ToArray();
        resp.StatusCode = 200;
        resp.ContentType = "text/plain";
        await resp.OutputStream.WriteAsync(okBytes, ct).ConfigureAwait(false);
        resp.Close();

        // Decrypt and deserialize
        var decryptedJson = VerifySignatureAndDecrypt(encryptContent);
        if (decryptedJson is null)
        {
            return;
        }

        WeComBotMessage? botMsg;
        try
        {
            botMsg = JsonSerializer.Deserialize(decryptedJson, WeComBotJsonContext.Default.WeComBotMessage);
        }
        catch (JsonException ex)
        {
            LogJsonParseFailed(ex);
            return;
        }

        if (botMsg is null)
        {
            return;
        }

        await ProcessBotMessageAsync(botMsg, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the request body stream, parses it as XML, and extracts the Encrypt field.
    /// Returns <c>null</c> if the body cannot be parsed or the Encrypt element is missing.
    /// </summary>
    private async Task<(string ToUserName, string Encrypt)?> ReadAndParseXmlBodyAsync(Stream body, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await body.CopyToAsync(ms, ct).ConfigureAwait(false);
        ms.Position = 0;

        string? toUserName;
        string? encryptContent;
        try
        {
            var doc = await XDocument.LoadAsync(ms, LoadOptions.None, ct).ConfigureAwait(false);
            toUserName = doc.Root?.Element("ToUserName")?.Value;
            encryptContent = doc.Root?.Element("Encrypt")?.Value;
        }
        catch (Exception ex)
        {
            LogXmlParseFailed(ex);
            return null;
        }

        if (string.IsNullOrEmpty(encryptContent))
        {
            return null;
        }

        return (toUserName ?? "", encryptContent);
    }

    /// <summary>
    /// Decrypts the encrypted content using the configured AES key.
    /// Returns the plaintext JSON string, or <c>null</c> if decryption fails.
    /// </summary>
    private string? VerifySignatureAndDecrypt(string encryptContent)
    {
        try
        {
            return WeComCrypto.Decrypt(_aesKey, encryptContent);
        }
        catch (Exception ex)
        {
            LogDecryptionFailed(ex);
            return null;
        }
    }

    /// <summary>
    /// Processes a decrypted WeCom AI Bot message: dedup, extract text, access control, publish.
    /// </summary>
    private async Task ProcessBotMessageAsync(WeComBotMessage msg, CancellationToken ct)
    {
        // Dedup by msgid
        if (msg.MsgId is not null && !_dedup.TryAdd(msg.MsgId))
        {
            return;
        }

        // Extract sender
        var userId = msg.From?.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        // Extract text from various message types
        var text = ExtractText(msg);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Determine sender context (senderId, senderName, threadId)
        var senderContext = ExtractSenderContext(msg);
        if (senderContext is null)
        {
            return;
        }

        var (senderId, senderName, threadId) = senderContext.Value;

        // Access control
        if (!_allowPolicy.IsAllowed(userId) &&
            !await _approvedSenders.IsApprovedAsync(ChannelName.WeCom.Value, userId).ConfigureAwait(false))
        {
            LogBlockedSender(userId);
            return;
        }

        // Store response_url for SendAsync
        if (msg.ResponseUrl is not null)
        {
            _responseUrls[senderId] = msg.ResponseUrl;
        }

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: senderId,
            SenderName: senderName,
            Text: text,
            ThreadId: threadId
        ), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines the sender ID, sender name, and optional thread ID from a WeCom bot message.
    /// Group messages use "group:{chatid}" as the sender ID; single chats use the user ID.
    /// Returns <c>null</c> if the sender cannot be determined.
    /// </summary>
    private static (string SenderId, string SenderName, string? ThreadId)? ExtractSenderContext(WeComBotMessage msg)
    {
        var userId = msg.From?.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var isGroup = string.Equals(msg.ChatType, "group", StringComparison.Ordinal);
        var senderId = isGroup && msg.ChatId is not null
            ? $"group:{msg.ChatId}"
            : userId;
        var threadId = isGroup ? msg.ChatId : null;

        return (senderId, userId, threadId);
    }

    /// <summary>
    /// Extracts a text string from the various WeCom message types.
    /// </summary>
    private static string? ExtractText(WeComBotMessage msg)
    {
        return msg.MsgType switch
        {
            "text" => msg.Text?.Content,
            "voice" => msg.Voice?.Content,
            "mixed" => ExtractMixedText(msg.Mixed),
            "image" => msg.Image?.Url is not null ? $"[image: {msg.Image.Url}]" : null,
            "file" => "[file attachment]",
            _ => null
        };
    }

    /// <summary>
    /// Concatenates all text segments from a mixed-content message.
    /// </summary>
    private static string? ExtractMixedText(WeComMixedContent? mixed)
    {
        if (mixed?.MsgItem is not { Count: > 0 })
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var item in mixed.MsgItem)
        {
            if (string.Equals(item.MsgType, "text", StringComparison.Ordinal) && item.Text?.Content is not null)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(item.Text.Content);
            }
            else if (string.Equals(item.MsgType, "image", StringComparison.Ordinal) && item.Image?.Url is not null)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append($"[image: {item.Image.Url}]");
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        if (!_responseUrls.TryGetValue(message.RecipientId, out var responseUrl))
        {
            LogNoResponseUrl(message.RecipientId);
            return;
        }

        var reply = new WeComReplyMessage
        {
            Text = new WeComReplyText { Content = message.Text }
        };

        var json = JsonSerializer.Serialize(reply, WeComBotJsonContext.Default.WeComReplyMessage);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.PostAsync(responseUrl, content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                LogSendError(body);
            }
        }
        catch (Exception ex)
        {
            LogSendFailed(ex);
        }
    }

    // ── LoggerMessage methods ────────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting WeCom AI Bot webhook listener on port {Port}")]
    private partial void LogStartingWebhook(int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to start WeCom AI Bot webhook listener on port {Port}")]
    private partial void LogListenerStartFailed(Exception exception, int port);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Invalid WeCom msg_signature -- rejecting request")]
    private partial void LogInvalidSignature();

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Blocked WeCom sender {UserId}")]
    private partial void LogBlockedSender(string userId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "WeCom webhook handler error")]
    private partial void LogWebhookError(Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "WeCom send error: {ResponseBody}")]
    private partial void LogSendError(string responseBody);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "WeCom send failed")]
    private partial void LogSendFailed(Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "WeCom webhook URL verification handled")]
    private partial void LogVerificationHandled();

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "WeCom AES decryption failed")]
    private partial void LogDecryptionFailed(Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "WeCom XML body parse failed")]
    private partial void LogXmlParseFailed(Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "WeCom JSON parse failed")]
    private partial void LogJsonParseFailed(Exception exception);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "No response_url stored for WeCom recipient {RecipientId} -- cannot send reply")]
    private partial void LogNoResponseUrl(string recipientId);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "Invalid WeCom EncodingAesKey")]
    private partial void LogInvalidAesKey(Exception exception);
}