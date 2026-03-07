using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Channels;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.WeChat;

/// <summary>
/// WeChat Work (WeCom) channel. Supports two modes:
/// <list type="bullet">
///   <item>Sending via WeCom robot webhook (requires <c>WebhookKey</c>).</item>
///   <item>Bidirectional via a bridge (requires <c>BridgeUrl</c>) — polls for inbound, POSTs to send.</item>
/// </list>
/// If only <c>WebhookKey</c> is configured, the channel is send-only (no receive polling).
/// </summary>
internal sealed partial class WeChatChannel : BridgePollingChannelBase<WeChatBridgeMessage, WeChatBridgeSendRequest>
{
    private readonly ILogger<WeChatChannel> _logger;

    private readonly string? _webhookKey;

    private readonly bool _hasBridge;

    /// <summary>Tracks the last seen timestamp (ms) for bridge polling.</summary>
    private long _sinceTimestamp;

    private const string WeComWebhookBaseUrl = "https://qyapi.weixin.qq.com/cgi-bin/webhook/send";

    public override ChannelName Name => ChannelName.WeChat;

    protected override ILogger Logger => _logger;

    protected override JsonTypeInfo<WeChatBridgeSendRequest> SendRequestTypeInfo => WeChatJsonContext.Default.WeChatBridgeSendRequest;

    public WeChatChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<WeChatChannel> logger,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders)
        : base(options, bus, httpClientFactory, approvedSenders, "wechat",
            ChannelName.WeChat.Value,
            bridgeConfigCheck: static cfg => cfg.BridgeUrl is not null || cfg.WebhookKey is not null)
    {
        _logger = logger;
        _webhookKey = ChannelCfg?.WebhookKey;
        _hasBridge = ChannelCfg?.BridgeUrl is not null;
        _sinceTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Override ExecuteAsync: if no bridge, enter webhook-only (send-only) mode.
    /// Otherwise, delegate to the base class poll loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            return;
        }

        if (!_hasBridge)
        {
            LogWebhookOnlyMode(_logger);
            // Send-only mode — no polling loop needed.
            // Keep the service alive so SendAsync works.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            return;
        }

        await base.ExecuteAsync(stoppingToken).ConfigureAwait(false);
    }

    protected override string GetPollUrl() => $"messages?since={_sinceTimestamp}";

    protected override async ValueTask<IReadOnlyList<WeChatBridgeMessage>?> DeserializePollResponseAsync(
        Stream responseStream, CancellationToken ct)
    {
        return await JsonSerializer.DeserializeAsync(
            responseStream, WeChatJsonContext.Default.ListWeChatBridgeMessage, ct).ConfigureAwait(false);
    }

    protected override string? GetSenderId(WeChatBridgeMessage item)
    {
        // Update the high-water mark for every message (including blocked senders)
        // to avoid re-fetching the same messages on next poll.
        if (item.Timestamp > _sinceTimestamp)
        {
            _sinceTimestamp = item.Timestamp;
        }

        return string.IsNullOrEmpty(item.FromUser) ? null : item.FromUser;
    }

    protected override ValueTask<InboundMessage?> MapIncomingAsync(
        WeChatBridgeMessage item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Content))
        {
            return new ValueTask<InboundMessage?>((InboundMessage?)null);
        }

        return new ValueTask<InboundMessage?>(new InboundMessage(
            Channel: Name,
            SenderId: item.FromUser,
            SenderName: item.FromUser,
            Text: item.Content
        ));
    }

    protected override string GetSendUrl(OutboundMessage message) => "send";

    protected override WeChatBridgeSendRequest MapToSendRequest(OutboundMessage message) =>
        new()
        {
            Content = message.Text,
            ToUser = message.RecipientId
        };

    /// <summary>
    /// Override SendAsync to support dual send paths: bridge (directed) vs webhook (broadcast).
    /// </summary>
    public override async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return;
        }

        // If a bridge is configured, prefer it for directed replies.
        if (_hasBridge)
        {
            await base.SendAsync(message, ct).ConfigureAwait(false);
            return;
        }

        // Otherwise, use the WeCom robot webhook (broadcast, no user targeting).
        if (_webhookKey is not null)
        {
            await SendViaWebhookAsync(message, ct).ConfigureAwait(false);
        }
    }

    private async Task SendViaWebhookAsync(OutboundMessage message, CancellationToken ct)
    {
        var req = new WeChatWebhookRequest
        {
            Text = new WeChatWebhookText { Content = message.Text }
        };

        var json = JsonSerializer.Serialize(req, WeChatJsonContext.Default.WeChatWebhookRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var url = $"{WeComWebhookBaseUrl}?key={Uri.EscapeDataString(_webhookKey!)}";
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "WeChat webhook-only mode (no bridge configured, send-only)")]
    private static partial void LogWebhookOnlyMode(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "WeChat send error: {ResponseBody}")]
    private static partial void LogSendError(ILogger logger, string responseBody);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "WeChat send failed")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}