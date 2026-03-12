using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Qq;

/// <summary>
/// NapCat/OneBot QQ messaging channel.
/// Receives messages via a persistent WebSocket connection and sends replies via the OneBot HTTP REST API.
/// Supports both private (DM) and group messages.
/// </summary>
public sealed partial class QqChannel : LifecycleBackgroundService, IChannel
{
    private const int MaxWebSocketMessageBytes = 1 * 1024 * 1024; // 1 MB

    private const int DeduplicatorCapacity = 10_000;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly IMessageBus _bus;

    private readonly bool _enabled;

    private readonly ILogger<QqChannel> _logger;

    private readonly HttpClient _http;

    private readonly string _wsUrl = "";

    private readonly string _apiBase = "";

    private readonly string? _token;

    private readonly BoundedDeduplicator<string> _dedup = new(DeduplicatorCapacity);

    public QqChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<QqChannel> logger,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _approvedSenders = approvedSenders;
        _http = httpClientFactory.CreateClient("qq");

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Qq.Value);
        if (cfg is not { Enabled: true } || cfg.WebSocketUrl is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _wsUrl = cfg.WebSocketUrl;
        _token = cfg.Token;
        _apiBase = cfg.ApiBaseUrl ?? DeriveApiBase(cfg.WebSocketUrl);
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom, StringComparer.Ordinal);
    }

    public ChannelName Name => ChannelName.Qq;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        LogStarting();
        var backoffMs = 1000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWebSocketAsync(stoppingToken);
                // If RunWebSocketAsync exits cleanly (server close), reset backoff
                backoffMs = 1000;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogConnectError(ex);
                await Task.Delay(backoffMs, stoppingToken);
                backoffMs = Math.Min(backoffMs * 2, 60_000);
            }
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            string url;
            byte[] body;

            if (message.RecipientId.StartsWith("group:", StringComparison.Ordinal))
            {
                var groupId = message.RecipientId["group:".Length..];
                url = $"{_apiBase}/send_group_msg";
                body = JsonSerializer.SerializeToUtf8Bytes(
                    new QqSendGroupMessage(groupId, message.Text),
                    QqJsonContext.Default.QqSendGroupMessage);
            }
            else
            {
                url = $"{_apiBase}/send_private_msg";
                body = JsonSerializer.SerializeToUtf8Bytes(
                    new QqSendPrivateMessage(message.RecipientId, message.Text),
                    QqJsonContext.Default.QqSendPrivateMessage);
            }

            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            if (_token is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
                request.Content = content;
                using var resp = await _http.SendAsync(request, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    LogSendFailed(await resp.Content.ReadAsStringAsync(ct));
                }
            }
            else
            {
                using var resp = await _http.PostAsync(url, content, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    LogSendFailed(await resp.Content.ReadAsStringAsync(ct));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSendError(ex);
        }
    }

    private async Task RunWebSocketAsync(CancellationToken ct)
    {
        var wsUri = BuildWebSocketUri();
        using var ws = new ClientWebSocket();

        if (_token is not null)
        {
            ws.Options.SetRequestHeader("Authorization", $"Bearer {_token}");
        }

        await ws.ConnectAsync(wsUri, ct);
        LogConnected(_wsUrl);

        // Reset backoff on successful connect (handled by caller)
        await foreach (var json in WebSocketReceiver.ReceiveMessagesAsync(ws, MaxWebSocketMessageBytes, ct).ConfigureAwait(false))
        {
            try
            {
                await HandleEventAsync(json, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogEventProcessingError(ex);
            }
        }

        await WebSocketReceiver.CloseGracefullyAsync(ws);
    }

    private async Task HandleEventAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Only process message events
        if (!root.TryGetProperty("post_type", out var postType) ||
            !string.Equals(postType.GetString(), "message", StringComparison.Ordinal))
        {
            return;
        }

        // Extract message_id (can be int or string)
        var messageId = ExtractMessageId(root);
        if (messageId is null)
        {
            return;
        }

        // Deduplicate
        if (!_dedup.TryAdd(messageId))
        {
            return;
        }

        // Extract sender info
        var userId = ExtractUserId(root);
        if (userId is null)
        {
            return;
        }

        var senderName = ExtractSenderName(root) ?? userId;

        // Extract text from message segments
        var text = ExtractTextFromMessage(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Determine if group or private
        var messageType = root.TryGetProperty("message_type", out var mt) ? mt.GetString() : null;
        var isGroup = string.Equals(messageType, "group", StringComparison.Ordinal);

        string senderId;
        string? threadId = null;

        if (isGroup)
        {
            var groupId = root.TryGetProperty("group_id", out var gid) ? ExtractNumericOrString(gid) : null;
            if (groupId is null)
            {
                return;
            }

            senderId = $"group:{groupId}";
            threadId = groupId;
        }
        else
        {
            senderId = userId;
        }

        // AllowFrom check: for group messages check the group ID, for private check the user ID
        var checkId = isGroup ? senderId : userId;
        if (!_allowPolicy.IsAllowed(checkId) &&
            !await _approvedSenders.IsApprovedAsync(ChannelName.Qq.Value, checkId))
        {
            LogBlockedUser(checkId);
            return;
        }

        // Prepend sender context for group chats so the LLM can distinguish users
        if (isGroup)
        {
            text = $"[sender: {senderName} ({userId})]\n{text}";
        }

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: senderId,
            SenderName: senderName,
            Text: text,
            ThreadId: threadId
        ), ct);
    }

    /// <summary>
    /// Extracts concatenated text from the OneBot message segments array.
    /// Handles both array-of-segments format and plain string raw_message fallback.
    /// </summary>
    private static string? ExtractTextFromMessage(JsonElement root)
    {
        // Try array-of-segments format first
        if (root.TryGetProperty("message", out var messageProp) &&
            messageProp.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var segment in messageProp.EnumerateArray())
            {
                if (segment.TryGetProperty("type", out var typeProp) &&
                    string.Equals(typeProp.GetString(), "text", StringComparison.Ordinal) &&
                    segment.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("text", out var textProp))
                {
                    var t = textProp.GetString();
                    if (t is not null)
                    {
                        sb.Append(t);
                    }
                }
            }

            if (sb.Length > 0)
            {
                return sb.ToString();
            }
        }

        // Fallback to raw_message string
        if (root.TryGetProperty("raw_message", out var rawMsg) &&
            rawMsg.ValueKind == JsonValueKind.String)
        {
            return rawMsg.GetString();
        }

        return null;
    }

    /// <summary>
    /// Extracts the message_id as a string, handling both integer and string JSON values.
    /// </summary>
    private static string? ExtractMessageId(JsonElement root)
    {
        if (!root.TryGetProperty("message_id", out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt64().ToString(),
            JsonValueKind.String => prop.GetString(),
            _ => null
        };
    }

    /// <summary>Extracts user_id as a string.</summary>
    private static string? ExtractUserId(JsonElement root)
    {
        if (!root.TryGetProperty("user_id", out var prop))
        {
            return null;
        }

        return ExtractNumericOrString(prop);
    }

    /// <summary>Extracts sender.nickname from the event payload.</summary>
    private static string? ExtractSenderName(JsonElement root)
    {
        if (root.TryGetProperty("sender", out var sender) &&
            sender.TryGetProperty("nickname", out var nickname) &&
            nickname.ValueKind == JsonValueKind.String)
        {
            return nickname.GetString();
        }

        return null;
    }

    /// <summary>Converts a JSON value that may be a number or string to a string.</summary>
    private static string? ExtractNumericOrString(JsonElement prop)
    {
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt64().ToString(),
            JsonValueKind.String => prop.GetString(),
            _ => null
        };
    }

    /// <summary>
    /// Derives the HTTP REST API base URL from the WebSocket URL.
    /// Replaces ws:// with http:// and wss:// with https://, then strips the path.
    /// </summary>
    internal static string DeriveApiBase(string wsUrl)
    {
        // Replace scheme
        var httpUrl = wsUrl;
        if (httpUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            httpUrl = "https://" + httpUrl["wss://".Length..];
        }
        else if (httpUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            httpUrl = "http://" + httpUrl["ws://".Length..];
        }

        // Strip path — keep only scheme + authority
        if (Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Authority}";
        }

        return httpUrl;
    }

    /// <summary>
    /// Builds the full WebSocket URI, appending the access_token query parameter if a token is configured.
    /// </summary>
    private Uri BuildWebSocketUri()
    {
        if (_token is null)
        {
            return new Uri(_wsUrl);
        }

        // Append access_token as query parameter
        var separator = _wsUrl.Contains('?') ? "&" : "?";
        return new Uri($"{_wsUrl}{separator}access_token={Uri.EscapeDataString(_token)}");
    }

    // ── LoggerMessage methods ────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting QQ/OneBot channel")]
    private partial void LogStarting();

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "QQ/OneBot WebSocket error, reconnecting")]
    private partial void LogConnectError(Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "QQ/OneBot WebSocket connected to {WsUrl}")]
    private partial void LogConnected(string wsUrl);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "QQ/OneBot send failed: {ResponseBody}")]
    private partial void LogSendFailed(string responseBody);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "QQ/OneBot send error")]
    private partial void LogSendError(Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Blocked QQ/OneBot user {SenderId}")]
    private partial void LogBlockedUser(string senderId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "QQ/OneBot event processing error")]
    private partial void LogEventProcessingError(Exception exception);
}