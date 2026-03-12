using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Transcription;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Mattermost;

/// <summary>
/// Mattermost channel. Receives messages via persistent WebSocket connection
/// and sends replies via the Mattermost REST API v4.
/// Supports streaming via message edits (PUT /api/v4/posts/{id}).
/// </summary>
public sealed partial class MattermostChannel : LifecycleBackgroundService, IChannel, IStreamingChannel
{
    private readonly string _serverUrl = "";

    private readonly string _botToken = "";

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _enabled;

    private readonly IMessageBus _bus;

    private readonly ILogger<MattermostChannel> _logger;

    private readonly HttpClient _http;

    private readonly VoiceTranscriptionService _voiceService;

    private readonly long _maxVoiceFileBytes;

    private volatile string? _selfId;

    public ChannelName Name => ChannelName.Mattermost;

    public MattermostChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<MattermostChannel> logger,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders,
        VoiceTranscriptionService voiceService)
    {
        _bus = bus;
        _logger = logger;
        _approvedSenders = approvedSenders;
        _http = httpClientFactory.CreateClient("mattermost");
        _voiceService = voiceService;
        _maxVoiceFileBytes = options.Value.Limits.MaxVoiceFileBytes;

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Mattermost.Value);
        if (cfg is not { Enabled: true } || cfg.MattermostUrl is null || cfg.BotToken is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _serverUrl = cfg.MattermostUrl.TrimEnd('/');
        _botToken = cfg.BotToken;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom, StringComparer.Ordinal);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        LogStarting();

        var backoffMs = 5_000;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_selfId is null)
                {
                    await FetchSelfIdAsync(stoppingToken);
                }

                await RunWebSocketAsync(stoppingToken);
                backoffMs = 5_000; // reset on clean exit
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogConnectError(backoffMs, ex);
                await Task.Delay(backoffMs, stoppingToken);
                backoffMs = Math.Min(backoffMs * 2, 60_000);
            }
        }
    }

    private async Task RunWebSocketAsync(CancellationToken ct)
    {
        // Convert http(s) to ws(s) for WebSocket URL
        var wsUrl = _serverUrl
                    .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                    .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                    + "/api/v4/websocket";

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), ct);
        LogConnected(wsUrl);

        // Send authentication challenge
        var authMsg = JsonSerializer.SerializeToUtf8Bytes(
            new MattermostAuthChallenge
            {
                Seq = 1,
                Action = "authentication_challenge",
                Data = new MattermostAuthData { Token = _botToken }
            },
            MattermostJsonContext.Default.MattermostAuthChallenge);
        await ws.SendAsync(authMsg, WebSocketMessageType.Text, true, ct);

        await foreach (var message in WebSocketReceiver.ReceiveMessagesAsync(ws, MaxWebSocketMessageBytes, ct).ConfigureAwait(false))
        {
            MattermostWsEvent? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize(message, MattermostJsonContext.Default.MattermostWsEvent);
            }
            catch (JsonException)
            {
                continue;
            }

            if (envelope is null)
            {
                continue;
            }

            if (string.Equals(envelope.Event, "posted", StringComparison.Ordinal))
            {
                await HandlePostedEventAsync(envelope, ct);
            }
        }

        await WebSocketReceiver.CloseGracefullyAsync(ws);
    }

    private async Task HandlePostedEventAsync(MattermostWsEvent envelope, CancellationToken ct)
    {
        var parsed = ParsePostFromEvent(envelope);
        if (parsed is null)
        {
            return;
        }

        var (userId, channelId, message, postDoc) = parsed.Value;
        using (postDoc)
        {
            var root = postDoc.RootElement;

            // MED-50: Transcribe audio attachments if the post has file_ids and voice transcription is enabled.
            if (_voiceService.IsEnabled && root.TryGetProperty("file_ids", out var fileIdsProp) &&
                fileIdsProp.ValueKind == JsonValueKind.Array)
            {
                var transcript = await TryTranscribeAttachmentsAsync(fileIdsProp, ct);
                if (transcript is not null)
                {
                    message = string.IsNullOrWhiteSpace(message)
                        ? $"[Voice] {transcript}"
                        : $"{message}\n[Voice] {transcript}";
                }
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (ShouldIgnorePost(root, userId))
            {
                return;
            }

            // AllowFrom check
            if (!_allowPolicy.IsAllowed(userId) &&
                !await _approvedSenders.IsApprovedAsync(ChannelName.Mattermost.Value, userId))
            {
                LogBlockedUser(userId);
                return;
            }

            await _bus.PublishAsync(new InboundMessage(
                Channel: Name,
                SenderId: channelId,
                SenderName: userId,
                Text: message
            ), ct);
        }
    }

    /// <summary>
    /// Parses the double-encoded post JSON from a Mattermost WebSocket event and extracts
    /// the user ID, channel ID, message text, and the parsed <see cref="JsonDocument"/>.
    /// Returns <c>null</c> if the envelope data is missing or required fields are absent.
    /// The caller is responsible for disposing the returned <see cref="JsonDocument"/>.
    /// </summary>
    private static (string UserId, string ChannelId, string? Message, JsonDocument PostDoc)? ParsePostFromEvent(MattermostWsEvent envelope)
    {
        // data.post is a double-encoded JSON string
        if (envelope.Data is not { } data)
        {
            return null;
        }

        if (!data.TryGetProperty("post", out var postProp))
        {
            return null;
        }

        var postJson = postProp.GetString();
        if (string.IsNullOrEmpty(postJson))
        {
            return null;
        }

        // Second parse: the post itself
        var postDoc = JsonDocument.Parse(postJson);
        var root = postDoc.RootElement;

        var userId = root.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
        var channelId = root.TryGetProperty("channel_id", out var cid) ? cid.GetString() : null;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(channelId))
        {
            postDoc.Dispose();
            return null;
        }

        var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : null;

        return (userId, channelId, message, postDoc);
    }

    /// <summary>
    /// Determines whether a parsed Mattermost post should be ignored.
    /// Returns <c>true</c> for own messages, system messages, and webhook/bot posts.
    /// </summary>
    private bool ShouldIgnorePost(JsonElement root, string userId)
    {
        // Ignore own messages
        if (userId == _selfId)
        {
            return true;
        }

        // Ignore system messages
        // Mattermost uses empty string for user posts; system messages have types like "system_join"
        if (root.TryGetProperty("type", out var typeProp) &&
            typeProp.GetString() is { } typeStr &&
            typeStr.Length > 0 &&
            !string.Equals(typeStr, "", StringComparison.Ordinal))
        {
            return true;
        }

        // Ignore webhook/bot posts (props.from_webhook)
        if (root.TryGetProperty("props", out var props) &&
            props.TryGetProperty("from_webhook", out var fromWebhook))
        {
            var webhookStr = fromWebhook.GetString();
            if (string.Equals(webhookStr, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var postReq = new MattermostCreatePostRequest
        {
            ChannelId = message.RecipientId,
            Message = message.Text
        };

        var json = JsonSerializer.Serialize(postReq, MattermostJsonContext.Default.MattermostCreatePostRequest);

        try
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/v4/posts");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
            httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(httpReq, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LogSendFailed(await resp.Content.ReadAsStringAsync(ct));
            }
        }
        catch (Exception ex)
        {
            LogSendError(ex);
        }
    }

    /// <summary>
    /// Attempts to transcribe the first audio attachment from a Mattermost post.
    /// Checks each file_id via GET /api/v4/files/{id}/info; if the MIME type starts with "audio/",
    /// downloads the file and transcribes it. Returns the transcription text, or null if no audio found
    /// or transcription failed.
    /// </summary>
    private async Task<string?> TryTranscribeAttachmentsAsync(JsonElement fileIdsArray, CancellationToken ct)
    {
        foreach (var fileIdEl in fileIdsArray.EnumerateArray())
        {
            var fileId = fileIdEl.GetString();
            if (string.IsNullOrEmpty(fileId))
            {
                continue;
            }

            try
            {
                // Fetch file info to check MIME type.
                using var infoReq = new HttpRequestMessage(HttpMethod.Get, $"api/v4/files/{fileId}/info");
                infoReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
                using var infoResp = await _http.SendAsync(infoReq, ct);
                if (!infoResp.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var infoStream = await infoResp.Content.ReadAsStreamAsync(ct);
                using var infoDoc = await JsonDocument.ParseAsync(infoStream, default, ct);
                var mimeType = infoDoc.RootElement.TryGetProperty("mime_type", out var mimeProp)
                    ? mimeProp.GetString()
                    : null;

                if (mimeType is null || !mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check file size (max 25 MB for Whisper API).
                var fileSize = infoDoc.RootElement.TryGetProperty("size", out var sizeProp)
                    ? sizeProp.GetInt64()
                    : 0L;
                if (fileSize > _maxVoiceFileBytes)
                {
                    LogVoiceFileTooLarge(fileId, fileSize);
                    continue;
                }

                // Download the audio file.
                using var downloadReq = new HttpRequestMessage(HttpMethod.Get, $"api/v4/files/{fileId}");
                downloadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
                using var downloadResp = await _http.SendAsync(downloadReq, ct);
                if (!downloadResp.IsSuccessStatusCode)
                {
                    continue;
                }

                var audioBytes = await downloadResp.Content.ReadAsByteArrayAsync(ct);
                var transcript = await _voiceService.TranscribeAsync(audioBytes, mimeType, ct);
                if (transcript is not null)
                {
                    LogTranscriptionComplete(fileId, transcript.Length);
                    return transcript;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogTranscriptionError(fileId, ex);
            }
        }

        return null;
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
            sendInitialAsync: async (cursor, c) => { return await CreatePostAsync(message.RecipientId, cursor, c).ConfigureAwait(false); },
            editMessageAsync: async (id, text, c) => { await UpdatePostAsync(id, text, c).ConfigureAwait(false); },
            ct: ct).ConfigureAwait(false);

        // If the placeholder failed, send as a new message.
        if (!result.PlaceholderCreated)
        {
            await SendAsync(message with { Text = result.Text }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Creates a post and returns its ID.</summary>
    private async Task<string?> CreatePostAsync(string channelId, string text, CancellationToken ct)
    {
        var postReq = new MattermostCreatePostRequest
        {
            ChannelId = channelId,
            Message = text
        };
        var json = JsonSerializer.Serialize(postReq, MattermostJsonContext.Default.MattermostCreatePostRequest);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/v4/posts");
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(httpReq, ct);

        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var postResp = await JsonSerializer.DeserializeAsync(stream, MattermostJsonContext.Default.MattermostPostResponse, ct);
        return postResp?.Id;
    }

    /// <summary>Updates an existing post by ID.</summary>
    private async Task UpdatePostAsync(string postId, string text, CancellationToken ct)
    {
        var updateReq = new MattermostUpdatePostRequest
        {
            Id = postId,
            Message = text
        };
        var json = JsonSerializer.Serialize(updateReq, MattermostJsonContext.Default.MattermostUpdatePostRequest);
        using var httpReq = new HttpRequestMessage(HttpMethod.Put, $"api/v4/posts/{postId}");
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(httpReq, ct);

        if (!resp.IsSuccessStatusCode)
        {
            LogUpdatePostFailed(postId, await resp.Content.ReadAsStringAsync(ct));
        }
    }

    private async Task FetchSelfIdAsync(CancellationToken ct)
    {
        using var selfReq = new HttpRequestMessage(HttpMethod.Get, "api/v4/users/me");
        selfReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        using var resp = await _http.SendAsync(selfReq, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var user = await JsonSerializer.DeserializeAsync(stream, MattermostJsonContext.Default.MattermostUser, ct);
        if (user?.Id is not null)
        {
            _selfId = user.Id;
            LogBotUserId(user.Id, user.Username);
        }
        else
        {
            throw new InvalidOperationException("Mattermost /api/v4/users/me returned no user ID.");
        }
    }

    private const int MaxWebSocketMessageBytes = 1 * 1024 * 1024; // 1 MB

    // ── LoggerMessage methods ────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting Mattermost channel")]
    private partial void LogStarting();

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Mattermost connection error, reconnecting in {BackoffMs}ms")]
    private partial void LogConnectError(int backoffMs, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Mattermost WebSocket connected to {WsUrl}")]
    private partial void LogConnected(string wsUrl);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Mattermost send failed: {ResponseBody}")]
    private partial void LogSendFailed(string responseBody);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Mattermost send error")]
    private partial void LogSendError(Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Blocked Mattermost user {UserId}")]
    private partial void LogBlockedUser(string userId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Mattermost bot user: {UserId} ({Username})")]
    private partial void LogBotUserId(string userId, string? username);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Failed to fetch Mattermost bot user ID")]
    private partial void LogFetchSelfIdFailed(Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "Failed to post Mattermost streaming draft")]
    private partial void LogDraftPostFailed(Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Failed to update Mattermost post {PostId}: {ResponseBody}")]
    private partial void LogUpdatePostFailed(string postId, string responseBody);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "Mattermost voice transcription complete for file {FileId} ({Length} chars)")]
    private partial void LogTranscriptionComplete(string fileId, int length);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Mattermost voice transcription error for file {FileId}")]
    private partial void LogTranscriptionError(string fileId, Exception exception);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "Mattermost voice file {FileId} too large ({FileSize} bytes, max 25 MB)")]
    private partial void LogVoiceFileTooLarge(string fileId, long fileSize);
}