using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Config.Channels;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Core.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Clawsharp.Channels.Telegram;

public sealed partial class TelegramChannel : LifecycleBackgroundService, IChannel, IStreamingChannel, IThinkingIndicator
{
    private const int MaxServerBackoffSeconds = 60;

    private readonly ResiliencePipeline _retryPipeline;

    private static readonly TimeSpan PipelineExhaustionDelay = TimeSpan.FromMinutes(1);

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly IMessageBus _bus;

    private readonly DmPolicy? _dmPolicy;

    private readonly bool _enabled;

    private readonly HttpClient _http;

    private readonly ILogger<TelegramChannel> _logger;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly PairingStore _pairingStore;

    private readonly string _token = "";

    private readonly bool _requireMention;

    private readonly VoiceTranscriptionService _voiceService;

    private readonly long _maxImageBytes;

    private readonly long _maxVoiceFileBytes;

    private volatile bool _permanentStop;

    private long _botId;

    private string? _botUsername;

    private long _offset;

    public TelegramChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<TelegramChannel> logger,
        PairingStore pairingStore,
        ApprovedSendersStore approvedSenders,
        IHttpClientFactory httpClientFactory,
        VoiceTranscriptionService voiceService,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _bus = bus;
        _logger = logger;
        _pairingStore = pairingStore;
        _approvedSenders = approvedSenders;
        _http = httpClientFactory.CreateClient("telegram");
        _voiceService = voiceService;
        _retryPipeline = pipelineProvider.GetPipeline(ChannelName.Telegram.Value);
        _maxImageBytes = options.Value.Limits.MaxImageBytes;
        _maxVoiceFileBytes = options.Value.Limits.MaxVoiceFileBytes;

        var config = options.Value;
        var cfg = config.Channels.GetValueOrDefault(ChannelName.Telegram.Value);
        if (cfg is not { Enabled: true } || cfg.Token is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _token = cfg.Token;
        _dmPolicy = cfg.DmPolicy;
        _requireMention = cfg.RequireMention;

        _allowPolicy = new AllowListPolicy(cfg.AllowFrom, StringComparer.OrdinalIgnoreCase, Normalize);
    }

    public ChannelName Name => ChannelName.Telegram;

    private enum PollAction
    {
        Continue,

        Stop,

        Backoff
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        LogStartingLongPollLoop(_logger);
        await FetchBotInfoAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _retryPipeline.ExecuteAsync(
                    async ct => await RunPollLoopAsync(ct),
                    stoppingToken);

                // RunPollLoopAsync signals permanent stop (401/403) by setting
                // _permanentStop and returning normally, which avoids wasteful
                // Polly retries on non-retriable authentication errors.
                if (_permanentStop)
                {
                    LogPermanentApiError(_logger);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPipelineExhausted(_logger, ex);
                await Task.Delay(PipelineExhaustionDelay, stoppingToken);
            }
        }
    }

    private async Task RunPollLoopAsync(CancellationToken stoppingToken)
    {
        var backoffSeconds = 0; // exponential backoff for 5xx errors; 0 = no backoff active
        while (!stoppingToken.IsCancellationRequested)
        {
            var url = ApiUrl($"getUpdates?offset={_offset}&timeout=30");
            using var resp = await _http.GetAsync(url, stoppingToken);

            // CRIT-04: Check HTTP status before deserializing to avoid tight-looping on permanent errors.
            if (!resp.IsSuccessStatusCode)
            {
                var action = await HandlePollErrorResponseAsync(resp, backoffSeconds, stoppingToken);
                switch (action.Action)
                {
                    case PollAction.Stop:
                        _permanentStop = true;
                        return;
                    case PollAction.Backoff:
                        backoffSeconds = action.NewBackoffSeconds;
                        continue;
                    default:
                        continue;
                }
            }

            // Reset backoff on success
            backoffSeconds = 0;

            await using var stream = await resp.Content.ReadAsStreamAsync(stoppingToken);
            var result = await JsonSerializer.DeserializeAsync(stream, TelegramJsonContext.Default.TelegramGetUpdatesResponse,
                stoppingToken);
            if (result?.Ok != true)
            {
                LogPollApiError(_logger, result?.Description ?? "unknown error", result?.ErrorCode ?? 0);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            foreach (var update in result.Result)
            {
                _offset = update.UpdateId + 1;
                await ProcessUpdateAsync(update, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Handles non-success HTTP responses from the Telegram getUpdates API.
    /// Returns the action to take (stop polling, or continue with optional backoff adjustment).
    /// </summary>
    private async Task<(PollAction Action, int NewBackoffSeconds)> HandlePollErrorResponseAsync(
        HttpResponseMessage resp, int backoffSeconds, CancellationToken ct)
    {
        var statusCode = (int)resp.StatusCode;
        switch (statusCode)
        {
            case 401:
                LogPollFatalHttpError(_logger, statusCode, "Bot token is invalid (401). Stopping polling.");
                return (PollAction.Stop, backoffSeconds);
            case 403:
                LogPollFatalHttpError(_logger, statusCode, "Bot is blocked or forbidden (403). Stopping polling.");
                return (PollAction.Stop, backoffSeconds);
            case 409:
                LogPollConflict(_logger);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return (PollAction.Continue, backoffSeconds);
            case 429:
                var retryAfter = resp.Headers.RetryAfter?.Delta?.TotalSeconds
                                 ?? resp.Headers.RetryAfter?.Date?.Subtract(DateTimeOffset.UtcNow).TotalSeconds
                                 ?? 30;
                if (retryAfter < 1)
                {
                    retryAfter = 1;
                }

                if (retryAfter > 300)
                {
                    retryAfter = 300;
                }

                LogPollRateLimited(_logger, (int)retryAfter);
                await Task.Delay(TimeSpan.FromSeconds(retryAfter), ct);
                return (PollAction.Continue, backoffSeconds);
            default:
                if (statusCode >= 500)
                {
                    var baseBackoff = 1;
                    if (backoffSeconds != 0)
                    {
                        baseBackoff = backoffSeconds * 2;
                    }

                    var newBackoff = Math.Min(baseBackoff, MaxServerBackoffSeconds);
                    LogPollServerError(_logger, statusCode, newBackoff);
                    await Task.Delay(TimeSpan.FromSeconds(newBackoff), ct);
                    return (PollAction.Backoff, newBackoff);
                }

                // Other 4xx: log and apply brief delay to avoid tight loop
                LogPollHttpError(_logger, statusCode);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                return (PollAction.Continue, backoffSeconds);
        }
    }

    /// <summary>
    /// Processes a single Telegram update: voice transcription, group mention filtering,
    /// auth check, image download, sender context, thread ID mapping, and bus publish.
    /// </summary>
    private async Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken ct)
    {
        var msg = update.Message;
        // Skip messages with neither text nor photo, or without a sender
        if (msg is null || msg.From is null)
        {
            return;
        }

        var text = msg.Text;

        // Transcribe voice/audio messages if transcription is configured
        if (text is null && (msg.Voice is not null || msg.Audio is not null))
        {
            text = await TranscribeVoiceAsync(msg, ct);
        }

        if (text is null && msg.Photo is null)
        {
            return;
        }

        // Group mention filtering: in group chats (negative chat IDs), only respond when @mentioned
        var isGroup = msg.Chat.Id < 0;
        if (isGroup && _requireMention)
        {
            if (!IsBotMentioned(msg, ref text))
            {
                return; // Skip — not mentioned in group
            }
        }

        if (!await IsUserAllowedAsync(msg.From))
        {
            if (_dmPolicy == DmPolicy.Pairing)
            {
                var code = await _pairingStore.GetOrCreateCodeAsync(
                    ChannelName.Telegram.Value,
                    msg.From.Id.ToString(),
                    msg.From.FirstName,
                    ct);
                LogPairingSent(_logger, msg.From.Id, code);
                await SendPairingCodeAsync(msg.Chat.Id, msg.MessageThreadId, code, ct);
                return;
            }

            LogBlockedUser(_logger, msg.From.Id, msg.From.Username);
            await SendDeniedAsync(msg.Chat.Id, msg.MessageThreadId, msg.From, ct);
            return;
        }

        // Download image attachments (best-effort, max 5 MB each)
        IReadOnlyList<ImageAttachment>? images = null;
        if (msg.Photo is { Length: > 0 } photos)
        {
            images = await DownloadImageAttachmentsAsync(photos, ct);
        }

        // Add sender context for group chats so the LLM can distinguish users
        if (isGroup && text is not null)
        {
            var senderName = msg.From.FirstName;
            if (msg.From.Username is { Length: > 0 } uname)
            {
                senderName = $"{senderName} (@{uname})";
            }

            text = $"[sender: {senderName}]\n{text}";
        }

        // Forum/topic thread isolation: when a message arrives in a forum topic
        // (supergroup with topics enabled), include the topic thread ID in the
        // session key and thread it through so replies target the correct topic.
        var threadId = msg.MessageThreadId;
        string senderId;
        if (threadId is not null)
        {
            senderId = $"{msg.Chat.Id}:topic_{threadId}";
        }
        else
        {
            senderId = msg.Chat.Id.ToString();
        }
        var threadIdStr = threadId?.ToString();

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: senderId,
            SenderName: msg.From.FirstName,
            Text: text ?? "",
            ThreadId: threadIdStr,
            Images: images
        ), ct);
    }

    /// <summary>
    /// Downloads the largest photo attachment from a Telegram message.
    /// Returns null if the download fails or the file exceeds the size limit.
    /// </summary>
    private async Task<IReadOnlyList<ImageAttachment>?> DownloadImageAttachmentsAsync(
        TelegramPhotoSize[] photos, CancellationToken ct)
    {
        // Telegram provides multiple resolutions; the last entry is the largest.
        var largest = photos[^1];
        if (largest.FileSize > _maxImageBytes && largest.FileSize != 0)
        {
            return null;
        }

        try
        {
            var file = await GetFileAsync(largest.FileId, ct);
            if (file?.FilePath is not { Length: > 0 } filePath)
            {
                return null;
            }

            // MED-26: Guard against path traversal in FilePath from Telegram API
            if (filePath.Contains("..", StringComparison.Ordinal))
            {
                LogImageDownloadFailed(_logger, new InvalidOperationException(
                    $"Telegram FilePath contains traversal sequence: {filePath}"));
                return null;
            }

            // MED-25: URL-encode the file path component
            var fileUrl = $"file/bot{_token}/{Uri.EscapeDataString(filePath)}";
            var bytes = await _http.GetByteArrayAsync(fileUrl, ct);
            if (bytes.Length <= _maxImageBytes)
            {
                return [ImageAttachment.Create(Convert.ToBase64String(bytes), DetectMime(filePath), _maxImageBytes)];
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogImageDownloadFailed(_logger, ex);
            return null;
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        if (!TryParseChatInfo(message, out var chatId, out var messageThreadId))
        {
            return;
        }

        // Send typing action (best-effort)
        try
        {
            await ExecuteAsync(new TelegramSendChatActionRequest
            {
                ChatId = chatId,
                MessageThreadId = messageThreadId,
                Url = ApiUrl("sendChatAction")
            }, ct);
        }
        catch
        {
            /* best-effort */
        }

        // Split long messages (Telegram limit: 4096 chars)
        foreach (var chunk in MessageChunker.Split(message.Text, ClawsharpConstants.TelegramMaxMessageLength))
        {
            var resp = await ExecuteAsync(new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = chunk,
                MessageThreadId = messageThreadId,
                Url = ApiUrl("sendMessage")
            }, ct);

            // MED-31: Stop sending remaining chunks if a chunk fails
            if (resp is null)
            {
                LogSendFailed(_logger, "null response");
                break;
            }

            if (!resp.Ok)
            {
                LogSendFailed(_logger, "ok=false");
                break;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartThinkingAsync(string recipientId, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            // Parse composite recipient ID which may include ":topic_{threadId}" suffix
            long? messageThreadId = null;
            var colonIdx = recipientId.IndexOf(":topic_", StringComparison.Ordinal);
            string chatIdStr;
            if (colonIdx >= 0)
            {
                chatIdStr = recipientId[..colonIdx];
                if (long.TryParse(recipientId.AsSpan(colonIdx + 7), out var tid))
                {
                    messageThreadId = tid;
                }
            }
            else
            {
                chatIdStr = recipientId;
            }

            if (!long.TryParse(chatIdStr, out var chatId))
            {
                return;
            }

            await ExecuteAsync(new TelegramSendChatActionRequest
            {
                ChatId = chatId,
                Action = "typing",
                MessageThreadId = messageThreadId,
                Url = ApiUrl("sendChatAction")
            }, ct);
        }
        catch
        {
            /* best-effort — never throw into caller */
        }
    }

    /// <inheritdoc />
    public Task StopThinkingAsync(string recipientId, CancellationToken ct = default)
    {
        // Telegram typing indicators expire automatically after ~5 seconds.
        // No explicit stop action is needed.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Serializes and dispatches an <see cref="IRequest{TResponse}"/> via HTTP POST,
    /// then deserializes the response body.
    /// </summary>
    private async Task<TResponse?> ExecuteAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, request.RequestTypeInfo);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(request.Url, content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            LogSendFailed(_logger, $"HTTP {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");
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

        if (!TryParseChatInfo(message, out var chatId, out var messageThreadId))
        {
            return;
        }

        // Use ThrottledStreamWriter for the core accumulate-and-edit loop.
        // Telegram uses long message IDs, so we convert to/from string at the boundary.
        var result = await ThrottledStreamWriter.WriteWithResultAsync(
            tokens,
            sendInitialAsync: async (cursor, c) =>
            {
                var postResp = await ExecuteAsync(new TelegramSendMessageRequest
                {
                    ChatId = chatId,
                    Text = cursor,
                    MessageThreadId = messageThreadId,
                    Url = ApiUrl("sendMessage")
                }, c).ConfigureAwait(false);
                if (postResp?.Ok == true && postResp.Result is not null)
                {
                    return postResp.Result.MessageId.ToString();
                }

                return null;
            },
            editMessageAsync: async (msgIdStr, text, c) =>
            {
                if (long.TryParse(msgIdStr, out var msgId))
                {
                    await EditDraftBestEffortAsync(chatId, msgId, text, messageThreadId, c).ConfigureAwait(false);
                }
            },
            ct: ct).ConfigureAwait(false);

        var finalText = result.Text;

        if (result.PlaceholderCreated && long.TryParse(result.MessageId, out var draftMsgId))
        {
            // ThrottledStreamWriter already did the final edit. Now handle overflow:
            // if the final text exceeds Telegram's limit, the draft edit was truncated
            // by EditDraftBestEffortAsync, so send remaining chunks as new messages.
            if (finalText.Length > ClawsharpConstants.TelegramMaxMessageLength)
            {
                // The draft already has the first chunk (truncated by EditDraftBestEffortAsync).
                // Send the overflow as separate messages.
                foreach (var chunk in MessageChunker.Split(
                             finalText[ClawsharpConstants.TelegramMaxMessageLength..],
                             ClawsharpConstants.TelegramMaxMessageLength))
                {
                    try
                    {
                        await ExecuteAsync(new TelegramSendMessageRequest
                        {
                            ChatId = chatId,
                            Text = chunk,
                            MessageThreadId = messageThreadId,
                            Url = ApiUrl("sendMessage")
                        }, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        /* best-effort overflow */
                    }
                }
            }
        }
        else
        {
            // Draft post failed -- fall back to regular send.
            await SendAsync(message with { Text = finalText }, ct).ConfigureAwait(false);
        }
    }

    private async Task EditDraftBestEffortAsync(long chatId, long messageId, string text, long? messageThreadId, CancellationToken ct)
    {
        try
        {
            // Truncate to Telegram's limit.
            var display = text;
            if (text.Length > ClawsharpConstants.TelegramMaxMessageLength)
            {
                display = text[..ClawsharpConstants.TelegramMaxMessageLength];
            }
            await ExecuteAsync(new TelegramEditMessageTextRequest
            {
                ChatId = chatId,
                MessageId = messageId,
                Text = display,
                MessageThreadId = messageThreadId,
                Url = ApiUrl("editMessageText")
            }, ct);
        }
        catch
        {
            // best-effort — never throw
        }
    }

    /// <summary>
    /// Parses the composite recipient ID (which may include a topic suffix) and optional
    /// thread ID string back into the raw chat ID and optional message_thread_id.
    /// The SenderId format for forum topics is "{chatId}:topic_{threadId}".
    /// </summary>
    private static bool TryParseChatInfo(OutboundMessage message, out long chatId, out long? messageThreadId)
    {
        messageThreadId = null;

        // Parse message_thread_id from ThreadId if present
        if (message.ThreadId is not null && long.TryParse(message.ThreadId, out var tid))
        {
            messageThreadId = tid;
        }

        // The RecipientId may be "{chatId}:topic_{threadId}" or just "{chatId}"
        var recipientId = message.RecipientId;
        var colonIdx = recipientId.IndexOf(":topic_", StringComparison.Ordinal);
        if (colonIdx >= 0)
        {
            return long.TryParse(recipientId.AsSpan(0, colonIdx), out chatId);
        }

        return long.TryParse(recipientId, out chatId);
    }

    private string ApiUrl(string method) => $"bot{_token}/{method}";

    /// <summary>Calls Telegram's getMe API to learn the bot's own username and ID.</summary>
    private async Task FetchBotInfoAsync(CancellationToken ct)
    {
        try
        {
            var url = ApiUrl("getMe");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LogBotInfoFailed(_logger, new HttpRequestException($"getMe returned HTTP {(int)resp.StatusCode}"));
                return;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync(stream, TelegramJsonContext.Default.TelegramGetMeResponse, ct);
            if (result?.Ok == true && result.Result is not null)
            {
                _botUsername = result.Result.Username;
                _botId = result.Result.Id;
                LogBotUsername(_logger, _botUsername ?? "(no username)");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBotInfoFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Checks whether the bot is @mentioned in the message entities.
    /// If mentioned, strips the mention text from <paramref name="text"/> and returns true.
    /// </summary>
    private bool IsBotMentioned(TelegramMessage msg, ref string? text)
    {
        if (msg.Entities is null || text is null)
        {
            return false;
        }

        // Iterate in reverse so offset removal doesn't invalidate later offsets
        for (var i = msg.Entities.Length - 1; i >= 0; i--)
        {
            var entity = msg.Entities[i];

            // @username mention (type "mention")
            if (entity.Type is "mention" && _botUsername is not null)
            {
                if (entity.Offset >= 0 && entity.Offset + entity.Length <= text.Length)
                {
                    var mentionText = text.Substring(entity.Offset, entity.Length);
                    if (mentionText.Equals($"@{_botUsername}", StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Remove(entity.Offset, entity.Length).Trim();
                        return true;
                    }
                }
            }
            // text_mention (for users without usernames, or when bot is mentioned by display name)
            else if (entity.Type is "text_mention" && entity.User?.Id == _botId && _botId != 0)
            {
                if (entity.Offset >= 0 && entity.Offset + entity.Length <= text.Length)
                {
                    text = text.Remove(entity.Offset, entity.Length).Trim();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Strips leading '@' and trims whitespace — normalises both username and ID entries.</summary>
    private static string Normalize(string entry)
    {
        return entry.TrimStart('@').Trim();
    }

    private async ValueTask<bool> IsUserAllowedAsync(TelegramUser user)
    {
        if (_allowPolicy.IsAllowAll)
        {
            return true;
        }

        // Check dynamic approved senders store
        if (await _approvedSenders.IsApprovedAsync(ChannelName.Telegram.Value, user.Id.ToString()))
        {
            return true;
        }

        // Accept if numeric ID or username (either form) is in the set
        if (_allowPolicy.IsAllowed(user.Id.ToString()))
        {
            return true;
        }

        if (user.Username is { Length: > 0 } name && _allowPolicy.IsAllowed(Normalize(name)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Calls Telegram's getFile API to resolve a file_id to a download path.
    ///     Returns null if the call fails or the file is not found.
    /// </summary>
    private async Task<TelegramFile?> GetFileAsync(string fileId, CancellationToken ct)
    {
        var url = ApiUrl($"getFile?file_id={fileId}");
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            await using var fileStream = await resp.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync(fileStream, TelegramJsonContext.Default.TelegramGetFileResponse, ct);
            if (result?.Ok == true)
            {
                return result.Result;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Maps a file extension to an IANA image MIME type. Defaults to image/jpeg.</summary>
    private static string DetectMime(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg" // .jpg, .jpeg, or any Telegram default
        };
    }

    /// <summary>Sends a pairing code to an unknown user so the operator can approve them via CLI.</summary>
    private async Task SendPairingCodeAsync(long chatId, long? messageThreadId, string code, CancellationToken ct)
    {
        var text = $"You need to pair first. Send this code to the operator: {code}";
        try
        {
            await ExecuteAsync(new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = text,
                MessageThreadId = messageThreadId,
                Url = ApiUrl("sendMessage")
            }, ct);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>Sends a one-time denial message so the user knows their ID to share with the operator.</summary>
    private async Task SendDeniedAsync(long chatId, long? messageThreadId, TelegramUser user, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are not authorised to use this bot.");
        sb.AppendLine();
        sb.AppendLine($"Your user ID:  {user.Id}");
        if (user.Username is { Length: > 0 } uname)
        {
            sb.AppendLine($"Your username: @{uname}");
        }

        sb.AppendLine();
        sb.Append("Ask the operator to add your user ID to channels.telegram.allowFrom in config.json.");

        try
        {
            await ExecuteAsync(new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = sb.ToString(),
                MessageThreadId = messageThreadId,
                Url = ApiUrl("sendMessage")
            }, ct);
        }
        catch
        {
            /* best-effort */
        }
    }

    private async Task<string?> TranscribeVoiceAsync(TelegramMessage msg, CancellationToken ct)
    {
        var fileId = msg.Voice?.FileId ?? msg.Audio?.FileId;
        var mimeType = msg.Voice?.MimeType ?? msg.Audio?.MimeType ?? "audio/ogg";

        if (fileId is null)
        {
            return null;
        }

        if (!_voiceService.IsEnabled)
        {
            // MED-30: Send error through channel, not into LLM context
            await SendTranscriptionErrorAsync(msg.Chat.Id, msg.MessageThreadId, "Voice message — transcription is not configured.", ct);
            return null;
        }

        // CRIT-05: Check file size before downloading to prevent OOM
        var fileSize = msg.Voice?.FileSize ?? msg.Audio?.FileSize;
        if (fileSize > _maxVoiceFileBytes)
        {
            LogVoiceFileTooLarge(_logger, fileSize.Value);
            await SendTranscriptionErrorAsync(msg.Chat.Id, msg.MessageThreadId, $"Voice file too large to transcribe (max {_maxVoiceFileBytes / (1024 * 1024)} MB).", ct);
            return null;
        }

        if (fileSize is null)
        {
            LogVoiceFileSizeUnknown(_logger, fileId);
        }

        try
        {
            LogTranscribingVoice(_logger, fileId);
            var file = await GetFileAsync(fileId, ct);

            if (file?.FilePath is null)
            {
                LogTranscriptionFileFailed(_logger);
                await SendTranscriptionErrorAsync(msg.Chat.Id, msg.MessageThreadId,
                    "Voice message — could not resolve file for transcription.", ct);
                return null;
            }

            // MED-26: Guard against path traversal in FilePath from Telegram API
            if (file.FilePath.Contains("..", StringComparison.Ordinal))
            {
                LogTranscriptionError(_logger, new InvalidOperationException(
                    $"Telegram FilePath contains traversal sequence: {file.FilePath}"));
                await SendTranscriptionErrorAsync(msg.Chat.Id, msg.MessageThreadId, "Voice message — transcription unavailable.", ct);
                return null;
            }

            // MED-25: URL-encode the file path component
            var fileUrl = $"file/bot{_token}/{Uri.EscapeDataString(file.FilePath)}";
            var audioBytes = await _http.GetByteArrayAsync(fileUrl, ct);
            var transcription = await _voiceService.TranscribeAsync(audioBytes, mimeType, ct);

            if (transcription is null)
            {
                await SendTranscriptionErrorAsync(msg.Chat.Id, msg.MessageThreadId, "Voice message — transcription failed.", ct);
                return null;
            }

            LogTranscriptionComplete(_logger, transcription.Length);

            if (msg.Caption is { Length: > 0 } caption)
            {
                return $"[Voice] {transcription}\n[Caption] {caption}";
            }

            return $"[Voice] {transcription}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTranscriptionError(_logger, ex);
            await SendTranscriptionErrorAsync(msg.Chat.Id, msg.MessageThreadId, "Voice message — transcription unavailable.", ct);
            return null;
        }
    }

    /// <summary>Sends a transcription error message to the user without polluting the LLM context.</summary>
    private async Task SendTranscriptionErrorAsync(long chatId, long? messageThreadId, string errorMessage, CancellationToken ct)
    {
        try
        {
            await ExecuteAsync(new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = errorMessage,
                MessageThreadId = messageThreadId,
                Url = ApiUrl("sendMessage")
            }, ct);
        }
        catch
        {
            /* best-effort */
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting long-poll loop")]
    private static partial void LogStartingLongPollLoop(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Blocked user Id={UserId} Username={Username}")]
    private static partial void LogBlockedUser(ILogger logger, long userId, string? username);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Image download failed")]
    private static partial void LogImageDownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Send failed: {ResponseBody}")]
    private static partial void LogSendFailed(ILogger logger, string responseBody);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Transcribing voice message file_id={FileId}")]
    private static partial void LogTranscribingVoice(ILogger logger, string fileId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Failed to resolve Telegram file path for transcription")]
    private static partial void LogTranscriptionFileFailed(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Transcription complete ({Length} chars)")]
    private static partial void LogTranscriptionComplete(ILogger logger, int length);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Voice transcription error")]
    private static partial void LogTranscriptionError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Sent pairing code to user Id={UserId} Code={Code}")]
    private static partial void LogPairingSent(ILogger logger, long userId, string code);

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug, Message = "Failed to post streaming draft")]
    private static partial void LogDraftPostFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 14, Level = LogLevel.Error, Message = "Telegram polling fatal HTTP error {StatusCode}: {Reason}")]
    private static partial void LogPollFatalHttpError(ILogger logger, int statusCode, string reason);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning,
        Message = "Telegram polling conflict (409 — another instance running). Backing off 10s.")]
    private static partial void LogPollConflict(ILogger logger);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning,
        Message = "Telegram polling rate-limited (429). Retrying after {RetryAfterSeconds}s.")]
    private static partial void LogPollRateLimited(ILogger logger, int retryAfterSeconds);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning,
        Message = "Telegram polling server error (HTTP {StatusCode}). Backing off {BackoffSeconds}s.")]
    private static partial void LogPollServerError(ILogger logger, int statusCode, int backoffSeconds);

    [LoggerMessage(EventId = 18, Level = LogLevel.Warning, Message = "Telegram polling HTTP error {StatusCode}. Retrying after 2s delay.")]
    private static partial void LogPollHttpError(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 19, Level = LogLevel.Warning, Message = "Telegram API error: {Description} (error_code={ErrorCode})")]
    private static partial void LogPollApiError(ILogger logger, string description, int errorCode);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "Voice file too large to transcribe ({FileSize} bytes, max 25 MB)")]
    private static partial void LogVoiceFileTooLarge(ILogger logger, long fileSize);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug, Message = "Voice file size unknown for file_id={FileId}; allowing download")]
    private static partial void LogVoiceFileSizeUnknown(ILogger logger, string fileId);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "Bot username resolved: {Username}")]
    private static partial void LogBotUsername(ILogger logger, string username);

    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "Failed to fetch bot info via getMe")]
    private static partial void LogBotInfoFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 24, Level = LogLevel.Critical,
        Message = "All retry attempts exhausted, restarting pipeline in 60s")]
    private static partial void LogPipelineExhausted(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 25, Level = LogLevel.Critical,
        Message = "Telegram polling stopped permanently due to authentication error (401/403). Check your bot token.")]
    private static partial void LogPermanentApiError(ILogger logger);
}