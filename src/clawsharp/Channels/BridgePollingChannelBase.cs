using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Clawsharp.Config.Channels;

namespace Clawsharp.Channels;

/// <summary>
/// Abstract base for bridge-polling channels (WhatsApp, BlueBubbles, WeChat).
/// Encapsulates: bridge URL validation, SSRF check, poll loop with Polly resilience,
/// JSON deserialization, message filtering via AllowListPolicy + ApprovedSendersStore,
/// MessageBus publish, and POST-based send.
/// </summary>
/// <typeparam name="TIncoming">The deserialized incoming message type from the bridge.</typeparam>
/// <typeparam name="TSend">The send request body type for outbound messages.</typeparam>
public abstract partial class BridgePollingChannelBase<TIncoming, TSend> : LifecycleBackgroundService, IChannel
    where TIncoming : class
    where TSend : class
{
    /// <summary>Normal polling interval between successful poll iterations.</summary>
    private static readonly TimeSpan NormalPollInterval = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan PipelineExhaustionDelay = TimeSpan.FromMinutes(1);

    private readonly AllowListPolicy _allowPolicy;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly ResiliencePipeline _retryPipeline;

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
    /// <param name="pipelineProvider">Polly resilience pipeline provider (keyed by channel name).</param>
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
        ResiliencePipelineProvider<string> pipelineProvider,
        string httpClientName,
        string channelConfigKey,
        Func<ChannelConfig, bool>? bridgeConfigCheck = null)
    {
        Bus = bus;
        Http = httpClientFactory.CreateClient(httpClientName);
        _approvedSenders = approvedSenders;
        _retryPipeline = pipelineProvider.GetPipeline(channelConfigKey);

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _retryPipeline.ExecuteAsync(async ct => await PollOnceAsync(ct).ConfigureAwait(false), stoppingToken);
                await Task.Delay(NormalPollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPipelineExhausted(Logger, Name.Value, ex);
                await Task.Delay(PipelineExhaustionDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Executes a single poll iteration: GET → deserialize → filter → publish.</summary>
    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var resp = await Http.GetAsync(GetPollUrl(), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var messages = await DeserializePollResponseAsync(stream, ct).ConfigureAwait(false);

        if (messages is null || messages.Count == 0)
        {
            return;
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

            var inbound = await MapIncomingAsync(msg, ct).ConfigureAwait(false);
            if (inbound is null)
            {
                continue;
            }

            await Bus.PublishAsync(inbound, ct).ConfigureAwait(false);
        }

        OnPollSuccess();
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

    // ─── LoggerMessage definitions (shared across all bridge-polling channels) ──

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "[{Channel}] Starting poll loop (bridge={BridgeUrl})")]
    private static partial void LogStartingPollLoop(ILogger logger, string channel, string bridgeUrl);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
        Message = "[{Channel}] Blocked sender {Sender}")]
    private static partial void LogBlockedSender(ILogger logger, string channel, string sender);

    [LoggerMessage(EventId = 105, Level = LogLevel.Error,
        Message = "[{Channel}] Send error: {ResponseBody}")]
    private static partial void LogSendError(ILogger logger, string channel, string responseBody);

    [LoggerMessage(EventId = 106, Level = LogLevel.Error,
        Message = "[{Channel}] Send failed")]
    private static partial void LogSendFailed(ILogger logger, string channel, Exception exception);

    [LoggerMessage(EventId = 107, Level = LogLevel.Error,
        Message = "[{Channel}] Bridge URL {BridgeUrl} blocked by SSRF guard: {Reason}")]
    private static partial void LogBridgeUrlBlocked(ILogger logger, string channel, string bridgeUrl, string reason);

    [LoggerMessage(EventId = 108, Level = LogLevel.Critical,
        Message = "[{Channel}] All retry attempts exhausted, restarting pipeline in 60s")]
    private static partial void LogPipelineExhausted(ILogger logger, string channel, Exception exception);
}