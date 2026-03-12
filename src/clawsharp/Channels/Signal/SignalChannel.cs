using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Core.Transcription;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.Signal;

/// <summary>
/// Channel that communicates with signal-cli's built-in HTTP server
/// (https://github.com/AsamK/signal-cli).
///
/// Receive: Server-Sent Events on  GET  /api/v1/events?account={number}
/// Send:    JSON-RPC 2.0            POST /api/v1/rpc
/// Health:                          GET  /api/v1/check
/// </summary>
public sealed partial class SignalChannel : LifecycleBackgroundService, IChannel
{
    private readonly string _bridgeUrl = "";

    private readonly string _phoneNumber = "";

    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _enabled;

    private readonly IMessageBus _bus;

    private readonly ILogger<SignalChannel> _logger;

    private readonly HttpClient _http;

    private readonly VoiceTranscriptionService _voiceService;

    private long _rpcIdCounter;

    private int _consecutiveFailures;

    private int _backoffMs = 1_000;

    private const int MinBackoffMs = 1_000;

    private const int MaxBackoffMs = 300_000;

    private const int WarnThreshold = 5;

    private const int ErrorThreshold = 50;

    public ChannelName Name => ChannelName.Signal;

    public SignalChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<SignalChannel> logger,
        IHttpClientFactory httpClientFactory,
        VoiceTranscriptionService voiceService,
        ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _http = httpClientFactory.CreateClient("signal");
        _voiceService = voiceService;
        _approvedSenders = approvedSenders;

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Signal.Value);
        if (cfg is not { Enabled: true } || cfg.BridgeUrl is null || cfg.PhoneNumber is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _bridgeUrl = cfg.BridgeUrl.TrimEnd('/');
        _phoneNumber = cfg.PhoneNumber;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        var ssrfError = await SsrfGuard.CheckAsync(new Uri(_bridgeUrl), stoppingToken);
        if (ssrfError is not null)
        {
            LogBridgeUrlBlocked(_logger, _bridgeUrl, ssrfError);
            return;
        }

        var sseUrl = $"api/v1/events?account={Uri.EscapeDataString(_phoneNumber)}";
        LogStartingSseStream(_logger, _bridgeUrl, _phoneNumber);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadSseAsync(sseUrl, stoppingToken).ConfigureAwait(false);
                _consecutiveFailures = 0;
                _backoffMs = MinBackoffMs;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                LogBridgeUnreachable(_logger, ex);
                var delay = TrackFailure();
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSseError(_logger, ex);
                var delay = TrackFailure();
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ConnectAndReadSseAsync(string sseUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            LogBridgeError(_logger, (int)response.StatusCode);
            await Task.Delay(5_000, ct);
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // SSE parsing state
        string? eventType = null;
        var dataLines = new List<string>();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                // Stream closed — outer loop will reconnect
                break;
            }

            ParseSseLine(line, ref eventType, dataLines, out var shouldDispatch);
            if (shouldDispatch && dataLines.Count > 0)
            {
                var data = string.Join('\n', dataLines);
                if (string.Equals(eventType, "receive", StringComparison.Ordinal))
                {
                    await HandleReceiveEventAsync(data, ct);
                }

                eventType = null;
                dataLines.Clear();
            }
        }
    }

    /// <summary>
    /// Parses a single SSE line, accumulating event type and data.
    /// Sets <paramref name="shouldDispatch"/> to true on blank lines (SSE event boundary).
    /// </summary>
    private static void ParseSseLine(
        string line, ref string? eventType, List<string> dataLines, out bool shouldDispatch)
    {
        shouldDispatch = false;

        if (line.StartsWith(':'))
        {
            // Keep-alive comment — ignore
            return;
        }

        if (line.Length == 0)
        {
            // Blank line = dispatch accumulated event
            shouldDispatch = true;
            return;
        }

        if (line.StartsWith("event:", StringComparison.Ordinal))
        {
            eventType = line["event:".Length..].Trim();
        }
        else if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            // Per SSE spec a single leading space after the colon is stripped
            var value = line["data:".Length..];
            if (value.Length > 0 && value[0] == ' ')
            {
                value = value[1..];
            }

            dataLines.Add(value);
        }
    }

    private async Task HandleReceiveEventAsync(string json, CancellationToken ct)
    {
        SignalSseEvent? sseEvent;
        try
        {
            sseEvent = JsonSerializer.Deserialize(json, SignalJsonContext.Default.SignalSseEvent);
        }
        catch (JsonException ex)
        {
            LogParseError(_logger, ex);
            return;
        }

        var env = sseEvent?.Envelope;
        if (env?.DataMessage is not { } dataMsg)
        {
            return;
        }

        var senderInfo = await ExtractSenderInfoAsync(env);
        if (senderInfo is null)
        {
            return;
        }

        var (sender, senderName) = senderInfo.Value;

        // Audio attachments: use the "getAttachment" JSON-RPC method to fetch
        // base64-encoded bytes, then transcribe via VoiceTranscriptionService.
        var audioText = await HandleAudioAttachmentAsync(dataMsg, sender, ct);
        if (audioText is not null)
        {
            await _bus.PublishAsync(new InboundMessage(
                Channel: Name,
                SenderId: sender,
                SenderName: senderName,
                Text: audioText
            ), ct);
            return;
        }

        if (dataMsg.Message is not { Length: > 0 } text)
        {
            return;
        }

        await _bus.PublishAsync(new InboundMessage(
            Channel: Name,
            SenderId: sender,
            SenderName: senderName,
            Text: text
        ), ct);
    }

    /// <summary>
    /// Extracts the sender phone number and display name from the envelope,
    /// returning null if the sender is empty or blocked by the allowlist.
    /// </summary>
    private async Task<(string Sender, string SenderName)?> ExtractSenderInfoAsync(SignalEnvelopeContent env)
    {
        var sender = env.SourceNumber ?? env.Source ?? "";
        if (string.IsNullOrEmpty(sender))
        {
            return null;
        }

        if (!_allowPolicy.IsAllowed(sender) &&
            !await _approvedSenders.IsApprovedAsync(ChannelName.Signal.Value, sender))
        {
            LogBlockedSender(_logger, sender);
            return null;
        }

        return (sender, env.SourceName ?? sender);
    }

    /// <summary>
    /// If the data message contains an audio attachment and no text, transcribes it
    /// and returns the transcript text. Returns null if not an audio-only message.
    /// </summary>
    private async Task<string?> HandleAudioAttachmentAsync(
        SignalDataMessage dataMsg, string sender, CancellationToken ct)
    {
        var audioAttachment = dataMsg.Attachments?.FirstOrDefault(a =>
            a.ContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true
            && a.Id is not null);

        if (audioAttachment is null || !string.IsNullOrEmpty(dataMsg.Message))
        {
            return null;
        }

        var transcript = await TranscribeAttachmentAsync(audioAttachment, sender, ct);
        return transcript ?? "[Voice message received — transcription unavailable]";
    }

    private async Task<string?> TranscribeAttachmentAsync(
        SignalAttachment attachment, string sender, CancellationToken ct)
    {
        if (!_voiceService.IsEnabled)
        {
            return null;
        }

        try
        {
            var result = await FetchAttachmentBytesAsync(attachment, sender, ct);
            if (result is null)
            {
                return null;
            }

            var (bytes, mime) = result.Value;
            var transcript = await _voiceService.TranscribeAsync(bytes, mime, ct);
            return transcript is not null ? $"[Voice] {transcript}" : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTranscribeError(_logger, ex);
            return null;
        }
    }

    /// <summary>
    /// Fetches attachment bytes via the signal-cli getAttachment JSON-RPC method.
    /// Returns the decoded bytes and MIME type, or null on failure.
    /// </summary>
    private async Task<(byte[] Data, string Mime)?> FetchAttachmentBytesAsync(
        SignalAttachment attachment, string sender, CancellationToken ct)
    {
        var rpcRequest = new SignalGetAttachmentRpcRequest
        {
            Id = Interlocked.Increment(ref _rpcIdCounter),
            Params = new SignalGetAttachmentParams
            {
                Account = _phoneNumber,
                Id = attachment.Id!,
                Recipient = sender,
            }
        };

        var json = JsonSerializer.Serialize(
            rpcRequest, SignalJsonContext.Default.SignalGetAttachmentRpcRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync("api/v1/rpc", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var rpcResp = await JsonSerializer.DeserializeAsync(
            stream, SignalJsonContext.Default.SignalGetAttachmentResponse, ct);

        if (rpcResp?.Error is { } err)
        {
            LogGetAttachmentError(_logger, err.Code, err.Message ?? "");
            return null;
        }

        var base64 = rpcResp?.Result?.Data;
        if (string.IsNullOrEmpty(base64))
        {
            return null;
        }

        var bytes = Convert.FromBase64String(base64);
        var mime = attachment.ContentType?.Split(';')[0].Trim() ?? "audio/ogg";
        return (bytes, mime);
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var rpcRequest = new SignalSendRpcRequest
        {
            Id = Interlocked.Increment(ref _rpcIdCounter),
            Params = new SignalSendParams
            {
                Account = _phoneNumber,
                Recipients = [message.RecipientId],
                Message = message.Text
            }
        };

        var json = JsonSerializer.Serialize(rpcRequest, SignalJsonContext.Default.SignalSendRpcRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.PostAsync("api/v1/rpc", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                LogSendError(_logger, body);
                return;
            }

            await using var respStream = await resp.Content.ReadAsStreamAsync(ct);
            var rpcResp = await JsonSerializer.DeserializeAsync(
                respStream, SignalJsonContext.Default.SignalJsonRpcResponse, ct);
            if (rpcResp?.Error is { } err)
            {
                LogSendRpcError(_logger, err.Code, err.Message ?? "");
            }
        }
        catch (Exception ex)
        {
            LogSendFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Tracks consecutive failures, logs warnings/errors at thresholds,
    /// and returns the current exponential backoff delay in milliseconds.
    /// </summary>
    private int TrackFailure()
    {
        _consecutiveFailures++;

        if (_consecutiveFailures == WarnThreshold)
        {
            LogBridgeHealthWarning(_logger, _consecutiveFailures);
        }
        else if (_consecutiveFailures == ErrorThreshold)
        {
            LogBridgeHealthError(_logger, _consecutiveFailures);
        }

        var delay = _backoffMs;
        _backoffMs = Math.Min(_backoffMs * 2, MaxBackoffMs);
        return delay;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Starting SSE stream (bridge={BridgeUrl}, phone={PhoneNumber})")]
    private static partial void LogStartingSseStream(ILogger logger, string bridgeUrl, string phoneNumber);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Bridge returned {StatusCode}, retrying")]
    private static partial void LogBridgeError(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Blocked sender {Sender}")]
    private static partial void LogBlockedSender(ILogger logger, string sender);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Bridge unreachable")]
    private static partial void LogBridgeUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "SSE stream error")]
    private static partial void LogSseError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error,
        Message = "Send error: {ResponseBody}")]
    private static partial void LogSendError(ILogger logger, string responseBody);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error,
        Message = "Send failed")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error,
        Message = "Bridge URL {BridgeUrl} blocked by SSRF guard: {Reason}")]
    private static partial void LogBridgeUrlBlocked(ILogger logger, string bridgeUrl, string reason);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Failed to parse SSE event JSON")]
    private static partial void LogParseError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error,
        Message = "JSON-RPC send error {Code}: {RpcMessage}")]
    private static partial void LogSendRpcError(ILogger logger, int code, string rpcMessage);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
        Message = "getAttachment JSON-RPC error {Code}: {RpcMessage}")]
    private static partial void LogGetAttachmentError(ILogger logger, int code, string rpcMessage);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "Failed to transcribe Signal voice attachment")]
    private static partial void LogTranscribeError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "Signal: {Count} consecutive failures -- check Signal bridge health")]
    private static partial void LogBridgeHealthWarning(ILogger logger, int count);

    [LoggerMessage(EventId = 14, Level = LogLevel.Error,
        Message = "Signal: {Count} consecutive failures -- bridge appears down, backoff active")]
    private static partial void LogBridgeHealthError(ILogger logger, int count);
}