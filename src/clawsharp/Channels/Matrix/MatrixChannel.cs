using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Matrix;

public sealed partial class MatrixChannel : LifecycleBackgroundService, IChannel, IStreamingChannel
{
    private readonly AllowListPolicy _roomAllowPolicy = AllowListPolicy.AllowAll;

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _requireMention;

    private readonly IMessageBus _bus;

    private readonly bool _enabled;

    private readonly HttpClient _http;

    private readonly ILogger<MatrixChannel> _logger;

    private readonly BoundedDeduplicator<string> _seenEvents = new(10_000);

    /// <summary>Matrix user ID for re-login (from config Username field, e.g. "@bot:matrix.org" or "bot").</summary>
    private readonly string? _userId;

    /// <summary>Matrix password for re-login (from config Password field).</summary>
    private readonly string? _password;

    /// <summary>Lock to prevent concurrent re-login attempts.</summary>
    private readonly SemaphoreSlim _reloginLock = new(1, 1);

    private string? _accessToken;

    private string? _nextBatch;

    private string? _selfId;

    private string? _selfLocalpart;

    private long _txnCounter;

    private static readonly string SyncTokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clawsharp", "matrix_sync_token.txt");

    public MatrixChannel(IOptions<AppConfig> options, IMessageBus bus, ILogger<MatrixChannel> logger, IHttpClientFactory httpClientFactory,
                         ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _http = httpClientFactory.CreateClient("matrix");
        _approvedSenders = approvedSenders;

        var config = options.Value;
        var cfg = config.Channels.GetValueOrDefault(ChannelName.Matrix.Value);
        if (cfg is not { Enabled: true } || cfg.AccessToken is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _roomAllowPolicy = new AllowListPolicy(cfg.AllowRooms);
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom);

        _requireMention = cfg.RequireMention;
        _accessToken = cfg.AccessToken;
        _userId = cfg.Username;
        _password = cfg.Password;
    }

    public ChannelName Name => ChannelName.Matrix;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        LogStartingSyncLoop(_logger);
        await FetchSelfIdAsync(stoppingToken);
        LoadSyncToken();
        await SyncOnceAsync(true, stoppingToken); // initial sync, skip messages

        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(false, stoppingToken);
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
                LogSyncError(_logger, ex);
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

        var roomId = Uri.EscapeDataString(message.RecipientId);
        var txnId = NextTxnId();
        var url = $"_matrix/client/v3/rooms/{roomId}/send/m.room.message/{txnId}";

        var resp = await ExecuteAsync(new MatrixSendMessageRequest
        {
            Body = message.Text,
            Url = url
        }, ct, HttpMethod.Put);

        if (resp is null)
        {
            LogSendFailed(_logger, "null response");
        }
    }

    /// <summary>
    /// Serializes and dispatches an <see cref="IRequest{TResponse}"/> via the specified HTTP method,
    /// then deserializes the response body. If the server responds with 401 (M_UNKNOWN_TOKEN),
    /// attempts a re-login and retries the request once.
    /// </summary>
    private async Task<TResponse?> ExecuteAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken ct,
        HttpMethod? method = null)
    {
        var httpMethod = method ?? HttpMethod.Post;
        var json = JsonSerializer.Serialize(request, request.RequestTypeInfo);

        // First attempt.
        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
        using (var req = CreateRequest(httpMethod, request.Url, content))
        {
            using var resp = await _http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonSerializer.DeserializeAsync(stream, request.ResponseTypeInfo, ct);
            }

            // MED-34: If 401, attempt re-login and retry once.
            if ((int)resp.StatusCode == 401 && await TryReloginAsync(ct))
            {
                // Fall through to retry below.
            }
            else
            {
                LogSendFailed(_logger, await resp.Content.ReadAsStringAsync(ct));
                return default;
            }
        }

        // Retry after successful re-login.
        using (var retryContent = new StringContent(json, Encoding.UTF8, "application/json"))
        using (var retryReq = CreateRequest(httpMethod, request.Url, retryContent))
        {
            using var retryResp = await _http.SendAsync(retryReq, ct);

            if (!retryResp.IsSuccessStatusCode)
            {
                LogSendFailed(_logger, await retryResp.Content.ReadAsStringAsync(ct));
                return default;
            }

            await using var retryStream = await retryResp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync(retryStream, request.ResponseTypeInfo, ct);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        if (_accessToken is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        return req;
    }

    /// <summary>
    /// Attempts to re-login to the Matrix homeserver using password auth to obtain a fresh access token.
    /// Returns true if re-login succeeded and the access token was refreshed, false otherwise.
    /// Only one re-login attempt runs at a time (protected by <see cref="_reloginLock"/>).
    /// </summary>
    private async Task<bool> TryReloginAsync(CancellationToken ct)
    {
        // Cannot re-login without credentials.
        if (_userId is null || _password is null)
        {
            LogReloginSkipped(_logger);
            return false;
        }

        // Ensure only one concurrent re-login attempt.
        if (!await _reloginLock.WaitAsync(0, ct))
        {
            // Another re-login is in progress; wait for it and check if the token was refreshed.
            await _reloginLock.WaitAsync(ct);
            _reloginLock.Release();
            return _accessToken is not null;
        }

        try
        {
            LogReloginAttempt(_logger);

            var loginResp = await ExecuteLoginAsync(ct);

            if (loginResp?.AccessToken is not null)
            {
                _accessToken = loginResp.AccessToken;
                LogReloginSuccess(_logger);
                return true;
            }

            LogReloginFailed(_logger, "login response did not contain an access token");
            return false;
        }
        catch (Exception ex)
        {
            LogReloginError(_logger, ex);
            return false;
        }
        finally
        {
            _reloginLock.Release();
        }
    }

    /// <summary>Calls POST /_matrix/client/v3/login with password credentials.</summary>
    private async Task<MatrixLoginResponse?> ExecuteLoginAsync(CancellationToken ct)
    {
        var loginRequest = new MatrixLoginRequest
        {
            Identifier = new MatrixUserIdentifier { User = _userId! },
            Password = _password!,
            Url = "_matrix/client/v3/login"
        };

        var json = JsonSerializer.Serialize(loginRequest, MatrixJsonContext.Default.MatrixLoginRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        // Do NOT use CreateRequest here -- we may have an expired/invalid token,
        // and the login endpoint does not require Authorization.
        using var req = new HttpRequestMessage(HttpMethod.Post, loginRequest.Url) { Content = content };
        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            LogReloginFailed(_logger, $"HTTP {(int)resp.StatusCode}: {body}");
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, MatrixJsonContext.Default.MatrixLoginResponse, ct);
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
                var roomId = Uri.EscapeDataString(message.RecipientId);
                var txnId = NextTxnId();
                var url = $"_matrix/client/v3/rooms/{roomId}/send/m.room.message/{txnId}";

                var postResp = await ExecuteAsync(new MatrixSendMessageRequest
                {
                    Body = cursor,
                    Url = url
                }, c, HttpMethod.Put).ConfigureAwait(false);
                return postResp?.EventId;
            },
            editMessageAsync: async (eventId, text, c) =>
            {
                await SendEditBestEffortAsync(message.RecipientId, eventId, text, c).ConfigureAwait(false);
            },
            ct: ct).ConfigureAwait(false);

        // If the placeholder failed, send as a new message.
        if (!result.PlaceholderCreated)
        {
            await SendAsync(message with { Text = result.Text }, ct).ConfigureAwait(false);
        }
    }

    private async Task SendEditBestEffortAsync(string recipientId, string eventId, string text, CancellationToken ct)
    {
        try
        {
            var roomId = Uri.EscapeDataString(recipientId);
            var txnId = NextTxnId();
            var url = $"_matrix/client/v3/rooms/{roomId}/send/m.room.message/{txnId}";

            await ExecuteAsync(new MatrixEditMessageRequest
            {
                Body = $"* {text}",
                NewContent = new MatrixNewContent { Body = text },
                RelatesTo = new MatrixRelatesTo { EventId = eventId },
                Url = url
            }, ct, HttpMethod.Put);
        }
        catch
        {
            // best-effort — never throw
        }
    }

    private string NextTxnId()
        => $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Interlocked.Increment(ref _txnCounter)}";

    private void LoadSyncToken()
    {
        try
        {
            if (File.Exists(SyncTokenPath))
            {
                var token = File.ReadAllText(SyncTokenPath).Trim();
                if (token.Length > 0)
                {
                    _nextBatch = token;
                }
            }
        }
        catch (Exception ex)
        {
            LogSyncTokenLoadFailed(_logger, ex);
        }
    }

    private void SaveSyncToken(string token)
    {
        try
        {
            var dir = Path.GetDirectoryName(SyncTokenPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(SyncTokenPath, token);
        }
        catch (Exception ex)
        {
            LogSyncTokenSaveFailed(_logger, ex);
        }
    }

    private async Task FetchSelfIdAsync(CancellationToken ct)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Get, "_matrix/client/v3/account/whoami");
            using var resp = await _http.SendAsync(req, ct);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            if (doc.RootElement.TryGetProperty("user_id", out var uid))
            {
                _selfId = uid.GetString();
                _selfLocalpart = _selfId?.Split(':')[0].TrimStart('@');
                LogLoggedInAs(_logger, _selfId);
            }
        }
        catch (Exception ex)
        {
            LogFetchUserIdFailed(_logger, ex);
        }
    }

    private async Task SyncOnceAsync(bool skipMessages, CancellationToken ct)
    {
        var url = BuildSyncUrl();
        var sync = await FetchSyncResponseAsync(url, ct);
        if (sync is null)
        {
            return;
        }

        _nextBatch = sync.NextBatch;
        if (_nextBatch is not null)
        {
            SaveSyncToken(_nextBatch);
        }

        if (!skipMessages && sync.Rooms?.Join is not null)
        {
            await ProcessSyncRoomsAsync(sync, ct);
        }
    }

    /// <summary>
    /// Constructs the Matrix /sync URL with an optional <c>since</c> parameter
    /// for incremental sync from the last known batch token.
    /// </summary>
    private string BuildSyncUrl()
    {
        var url = "_matrix/client/v3/sync?timeout=30000";
        if (_nextBatch is not null)
        {
            url += $"&since={Uri.EscapeDataString(_nextBatch)}";
        }

        return url;
    }

    /// <summary>
    /// Sends the sync HTTP request. If the server responds with 401 (M_UNKNOWN_TOKEN),
    /// attempts a re-login via <see cref="TryReloginAsync"/> and retries the request once.
    /// Returns the deserialized <see cref="MatrixSyncResponse"/>, or <c>null</c> on failure.
    /// </summary>
    private async Task<MatrixSyncResponse?> FetchSyncResponseAsync(string url, CancellationToken ct)
    {
        using var syncReq = CreateRequest(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(syncReq, ct);

        if (resp.IsSuccessStatusCode)
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync(stream, MatrixJsonContext.Default.MatrixSyncResponse, ct);
        }

        // MED-34: Handle expired token during sync — attempt re-login and retry once.
        if ((int)resp.StatusCode != 401 || !await TryReloginAsync(ct))
        {
            return null;
        }

        using var retryReq = CreateRequest(HttpMethod.Get, url);
        using var retryResp = await _http.SendAsync(retryReq, ct);
        if (!retryResp.IsSuccessStatusCode)
        {
            return null;
        }

        await using var retryStream = await retryResp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(retryStream, MatrixJsonContext.Default.MatrixSyncResponse, ct);
    }

    private async Task ProcessSyncRoomsAsync(MatrixSyncResponse sync, CancellationToken ct)
    {
        if (sync.Rooms?.Join is null)
        {
            return;
        }

        foreach (var (roomId, room) in sync.Rooms.Join)
        {
            if (!_roomAllowPolicy.IsAllowed(roomId))
            {
                continue;
            }

            if (room.Timeline?.Events is null)
            {
                continue;
            }

            foreach (var ev in room.Timeline.Events)
            {
                if (ev.Type != "m.room.message")
                {
                    continue;
                }

                if (!_seenEvents.TryAdd(ev.EventId))
                {
                    continue;
                }

                if (ev.Sender == _selfId)
                {
                    continue;
                }

                // Per-user allowlist check (static AllowFrom + dynamic approved senders)
                if (!_allowPolicy.IsAllowed(ev.Sender) &&
                    !await _approvedSenders.IsApprovedAsync(ChannelName.Matrix.Value, ev.Sender))
                {
                    LogBlockedUser(_logger, ev.Sender);
                    continue;
                }

                if (!ev.Content.TryGetProperty("msgtype", out var mt) || mt.GetString() != "m.text")
                {
                    continue;
                }

                if (!ev.Content.TryGetProperty("body", out var body))
                {
                    continue;
                }

                var text = body.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // RequireMention: only respond when bot's localpart is in the message
                if (_requireMention && _selfLocalpart is not null)
                {
                    if (!text.Contains(_selfLocalpart, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                await _bus.PublishAsync(new InboundMessage(
                    Channel: Name,
                    SenderId: roomId,
                    SenderName: ev.Sender,
                    Text: text
                ), ct);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting sync loop")]
    private static partial void LogStartingSyncLoop(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Sync error")]
    private static partial void LogSyncError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Send failed: {ResponseBody}")]
    private static partial void LogSendFailed(ILogger logger, string responseBody);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Logged in as {UserId}")]
    private static partial void LogLoggedInAs(ILogger logger, string? userId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Failed to fetch user ID")]
    private static partial void LogFetchUserIdFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Blocked user {UserId}")]
    private static partial void LogBlockedUser(ILogger logger, string userId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Failed to post streaming draft")]
    private static partial void LogDraftPostFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Failed to load Matrix sync token from disk")]
    private static partial void LogSyncTokenLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Failed to save Matrix sync token to disk")]
    private static partial void LogSyncTokenSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Matrix token expired (401). Attempting re-login...")]
    private static partial void LogReloginAttempt(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Matrix re-login succeeded. Access token refreshed.")]
    private static partial void LogReloginSuccess(ILogger logger);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Matrix re-login failed: {Reason}")]
    private static partial void LogReloginFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "Matrix re-login error")]
    private static partial void LogReloginError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning,
        Message =
            "Matrix 401 received but no username/password configured for re-login. Set channels.matrix.username and channels.matrix.password to enable automatic token refresh.")]
    private static partial void LogReloginSkipped(ILogger logger);
}