using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Channels;

namespace Clawsharp.Channels;

/// <summary>
/// Abstract base for bridge-polling channels (WhatsApp, BlueBubbles, WeChat).
/// Encapsulates: bridge URL validation, SSRF check, poll loop with exponential backoff,
/// JSON deserialization, message filtering via AllowListPolicy + ApprovedSendersStore,
/// MessageBus publish, consecutive failure tracking with health warnings, and POST-based send.
/// </summary>
/// <typeparam name="TIncoming">The deserialized incoming message type from the bridge.</typeparam>
/// <typeparam name="TSend">The send request body type for outbound messages.</typeparam>
public abstract partial class BridgePollingChannelBase<TIncoming, TSend> : LifecycleBackgroundService, IChannel
    where TIncoming : class
    where TSend : class
{
    /// <summary>Number of consecutive poll failures before logging a health warning.</summary>
    private const int WarnAfterFailures = 5;

    /// <summary>Maximum consecutive poll failures before the channel stops retrying.</summary>
    private const int MaxConsecutiveFailures = 50;

    /// <summary>Normal polling interval in milliseconds.</summary>
    private const int NormalPollIntervalMs = 3000;

    /// <summary>Delay after a non-HTTP error in milliseconds (starting backoff).</summary>
    private const int ErrorDelayMs = 5000;

    /// <summary>Maximum backoff delay in milliseconds (5 minutes).</summary>
    private const int MaxBackoffMs = 300_000;

    private readonly AllowListPolicy _allowPolicy;

    private readonly ApprovedSendersStore _approvedSenders;

    private int _consecutiveFailures;

    /// <summary>The message bus for publishing inbound messages to the AgentLoop.</summary>
    protected IMessageBus Bus { get; }

    /// <summary>The HTTP client (named, with BaseAddress set to the bridge URL).</summary>
    protected HttpClient Http { get; }

    /// <summary>Whether this channel is enabled (bridge URL configured and enabled in config).</summary>
    protected bool Enabled { get; }

    /// <summary>The resolved bridge URL (trimmed of trailing slashes).</summary>
    protected string BridgeUrl { get; }

    /// <summary>The raw channel config (non-null when <see cref="Enabled"/> is true).</summary>
    protected ChannelConfig? ChannelCfg { get; }

    // ─── Abstract members ────────────────────────────────────────────────

    /// <inheritdoc/>
    public abstract ChannelName Name { get; }

    /// <summary>
    /// Returns the poll URL path (relative to the bridge BaseAddress).
    /// Called once per poll iteration. May include query parameters.
    /// </summary>
    protected abstract string GetPollUrl();

    /// <summary>
    /// Deserializes the poll response stream into a list of incoming messages.
    /// Returns null or empty if no messages are available.
    /// </summary>
    protected abstract ValueTask<IReadOnlyList<TIncoming>?> DeserializePollResponseAsync(
        Stream responseStream, CancellationToken ct);

    /// <summary>
    /// Filters and maps a single incoming message to an <see cref="InboundMessage"/>.
    /// Return null to skip the message (own message, empty body, etc.).
    /// The base class handles AllowFrom/ApprovedSenders checks after this method
    /// returns a non-null result.
    /// </summary>
    protected abstract ValueTask<InboundMessage?> MapIncomingAsync(
        TIncoming item, CancellationToken ct);

    /// <summary>
    /// Extracts the sender ID from an incoming message for AllowFrom/ApprovedSenders checking.
    /// Return null to skip the message entirely.
    /// </summary>
    protected abstract string? GetSenderId(TIncoming item);

    /// <summary>
    /// Called after a successful poll completes (all messages processed).
    /// Override to update high-water-mark timestamps, etc.
    /// </summary>
    protected virtual void OnPollSuccess()
    {
    }

    /// <summary>
    /// Returns the send URL path (relative to the bridge BaseAddress).
    /// </summary>
    protected abstract string GetSendUrl(OutboundMessage message);

    /// <summary>
    /// Maps an <see cref="OutboundMessage"/> to the bridge-specific send request body.
    /// </summary>
    protected abstract TSend MapToSendRequest(OutboundMessage message);

    /// <summary>
    /// Returns the source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="TSend"/>.
    /// Required for AOT-safe serialization.
    /// </summary>
    protected abstract JsonTypeInfo<TSend> SendRequestTypeInfo { get; }

    /// <summary>
    /// Optional hook called before the poll loop starts (after SSRF check succeeds).
    /// Override for channel-specific initialization (e.g., BlueBubbles HTTP-over-HTTPS warning).
    /// </summary>
    protected virtual Task OnBeforePollLoopAsync(CancellationToken ct) => Task.CompletedTask;

    // ─── Logger (abstract — each derived class has its own partial LoggerMessage methods) ───

    /// <summary>The logger instance for this channel.</summary>
    protected abstract ILogger Logger { get; }

    // ─── Constructor ─────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the bridge-polling channel base.
    /// </summary>
    /// <param name="options">App configuration containing channel settings.</param>
    /// <param name="bus">The inbound message bus.</param>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    /// <param name="approvedSenders">Dynamic approved senders store.</param>
    /// <param name="httpClientName">Named HTTP client identifier (e.g., "whatsapp", "bluebubbles", "wechat").</param>
    /// <param name="channelConfigKey">The config dictionary key (e.g., "whatsapp", "bluebubbles", "wechat").</param>
    /// <param name="bridgeConfigCheck">
    /// Optional predicate to validate bridge configuration beyond the default BridgeUrl non-null check.
    /// Receives the <see cref="ChannelConfig"/> and returns true if the bridge is properly configured.
    /// Default: <c>cfg => cfg.BridgeUrl is not null</c>.
    /// </param>
    protected BridgePollingChannelBase(
        IOptions<AppConfig> options,
        IMessageBus bus,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders,
        string httpClientName,
        string channelConfigKey,
        Func<ChannelConfig, bool>? bridgeConfigCheck = null)
    {
        Bus = bus;
        Http = httpClientFactory.CreateClient(httpClientName);
        _approvedSenders = approvedSenders;

        bridgeConfigCheck ??= static cfg => cfg.BridgeUrl is not null;

        var cfg = options.Value.Channels.GetValueOrDefault(channelConfigKey);
        if (cfg is not { Enabled: true } || !bridgeConfigCheck(cfg))
        {
            Enabled = false;
            BridgeUrl = "";
            _allowPolicy = AllowListPolicy.AllowAll;
            return;
        }

        ChannelCfg = cfg;
        Enabled = true;
        BridgeUrl = cfg.BridgeUrl?.TrimEnd('/') ?? "";
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom);
    }

    // ─── ExecuteAsync (poll loop) ────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            return;
        }

        var ssrfError = await SsrfGuard.CheckAsync(new Uri(BridgeUrl), stoppingToken).ConfigureAwait(false);
        if (ssrfError is not null)
        {
            LogBridgeUrlBlocked(Logger, Name.Value, BridgeUrl, ssrfError);
            return;
        }

        await OnBeforePollLoopAsync(stoppingToken).ConfigureAwait(false);
        LogStartingPollLoop(Logger, Name.Value, BridgeUrl);

        var currentBackoffMs = ErrorDelayMs;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var resp = await Http.GetAsync(GetPollUrl(), stoppingToken).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    LogBridgeError(Logger, Name.Value, (int)resp.StatusCode);
                    TrackFailure(ref currentBackoffMs);
                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        LogMaxFailuresReached(Logger, Name.Value, MaxConsecutiveFailures, BridgeUrl);
                        return;
                    }

                    await Task.Delay(currentBackoffMs, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Reset backoff on any successful HTTP response
                _consecutiveFailures = 0;
                currentBackoffMs = ErrorDelayMs;

                await using var stream = await resp.Content.ReadAsStreamAsync(stoppingToken).ConfigureAwait(false);
                var messages = await DeserializePollResponseAsync(stream, stoppingToken).ConfigureAwait(false);

                if (messages is null || messages.Count == 0)
                {
                    await Task.Delay(NormalPollIntervalMs, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var msg in messages)
                {
                    var senderId = GetSenderId(msg);
                    if (string.IsNullOrEmpty(senderId))
                    {
                        continue;
                    }

                    // Static AllowFrom + dynamic approved senders
                    if (!_allowPolicy.IsAllowed(senderId) &&
                        !await _approvedSenders.IsApprovedAsync(Name.Value, senderId).ConfigureAwait(false))
                    {
                        LogBlockedSender(Logger, Name.Value, senderId);
                        continue;
                    }

                    var inbound = await MapIncomingAsync(msg, stoppingToken).ConfigureAwait(false);
                    if (inbound is null)
                    {
                        continue;
                    }

                    await Bus.PublishAsync(inbound, stoppingToken).ConfigureAwait(false);
                }

                OnPollSuccess();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                LogBridgeUnreachable(Logger, Name.Value, ex);
                TrackFailure(ref currentBackoffMs);
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    LogMaxFailuresReached(Logger, Name.Value, MaxConsecutiveFailures, BridgeUrl);
                    return;
                }

                await Task.Delay(Math.Min(currentBackoffMs, MaxBackoffMs), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPollError(Logger, Name.Value, ex);
                TrackFailure(ref currentBackoffMs);
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    LogMaxFailuresReached(Logger, Name.Value, MaxConsecutiveFailures, BridgeUrl);
                    return;
                }

                await Task.Delay(currentBackoffMs, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    // ─── SendAsync (POST to bridge) ──────────────────────────────────────

    /// <inheritdoc/>
    public virtual async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return;
        }

        var req = MapToSendRequest(message);
        var json = JsonSerializer.Serialize(req, SendRequestTypeInfo);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await Http.PostAsync(GetSendUrl(message), content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                LogSendError(Logger, Name.Value, body);
            }
        }
        catch (Exception ex)
        {
            LogSendFailed(Logger, Name.Value, ex);
        }
    }

    // ─── Failure tracking ────────────────────────────────────────────────

    private void TrackFailure(ref int currentBackoffMs)
    {
        _consecutiveFailures++;

        if (_consecutiveFailures == WarnAfterFailures)
        {
            LogBridgeHealthWarning(Logger, Name.Value, _consecutiveFailures, BridgeUrl);
        }
        else if (_consecutiveFailures > WarnAfterFailures && _consecutiveFailures % 10 == 0)
        {
            LogBridgeHealthError(Logger, Name.Value, _consecutiveFailures, BridgeUrl);
        }

        // Exponential backoff: double each failure, cap at MaxBackoffMs
        currentBackoffMs = Math.Min(currentBackoffMs * 2, MaxBackoffMs);
    }

    // ─── LoggerMessage definitions (shared across all bridge-polling channels) ──

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "[{Channel}] Starting poll loop (bridge={BridgeUrl})")]
    private static partial void LogStartingPollLoop(ILogger logger, string channel, string bridgeUrl);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning,
        Message = "[{Channel}] Bridge returned {StatusCode}, retrying")]
    private static partial void LogBridgeError(ILogger logger, string channel, int statusCode);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
        Message = "[{Channel}] Blocked sender {Sender}")]
    private static partial void LogBlockedSender(ILogger logger, string channel, string sender);

    [LoggerMessage(EventId = 103, Level = LogLevel.Error,
        Message = "[{Channel}] Bridge unreachable")]
    private static partial void LogBridgeUnreachable(ILogger logger, string channel, Exception exception);

    [LoggerMessage(EventId = 104, Level = LogLevel.Error,
        Message = "[{Channel}] Poll error")]
    private static partial void LogPollError(ILogger logger, string channel, Exception exception);

    [LoggerMessage(EventId = 105, Level = LogLevel.Error,
        Message = "[{Channel}] Send error: {ResponseBody}")]
    private static partial void LogSendError(ILogger logger, string channel, string responseBody);

    [LoggerMessage(EventId = 106, Level = LogLevel.Error,
        Message = "[{Channel}] Send failed")]
    private static partial void LogSendFailed(ILogger logger, string channel, Exception exception);

    [LoggerMessage(EventId = 107, Level = LogLevel.Error,
        Message = "[{Channel}] Bridge URL {BridgeUrl} blocked by SSRF guard: {Reason}")]
    private static partial void LogBridgeUrlBlocked(ILogger logger, string channel, string bridgeUrl, string reason);

    [LoggerMessage(EventId = 108, Level = LogLevel.Warning,
        Message =
            "[{Channel}] Bridge appears unreachable after {FailureCount} consecutive failures (bridge={BridgeUrl}); channel will keep retrying")]
    private static partial void LogBridgeHealthWarning(ILogger logger, string channel, int failureCount, string bridgeUrl);

    [LoggerMessage(EventId = 109, Level = LogLevel.Error,
        Message = "[{Channel}] Bridge still unreachable after {FailureCount} consecutive failures (bridge={BridgeUrl})")]
    private static partial void LogBridgeHealthError(ILogger logger, string channel, int failureCount, string bridgeUrl);

    [LoggerMessage(EventId = 110, Level = LogLevel.Critical,
        Message = "[{Channel}] Bridge exceeded maximum consecutive failures ({MaxFailures}) (bridge={BridgeUrl}); stopping channel")]
    private static partial void LogMaxFailuresReached(ILogger logger, string channel, int maxFailures, string bridgeUrl);
}