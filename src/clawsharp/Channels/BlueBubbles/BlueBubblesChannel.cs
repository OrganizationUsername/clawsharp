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

namespace Clawsharp.Channels.BlueBubbles;

/// <summary>
/// Channel that communicates with a BlueBubbles server
/// (https://github.com/BlueBubblesApp/bluebubbles-server) for iMessage.
/// Polls GET /api/v1/message for inbound messages and
/// sends via POST /api/v1/message/text.
/// </summary>
internal sealed partial class BlueBubblesChannel : BridgePollingChannelBase<BlueBubblesMessage, BlueBubblesSendRequest>
{
    private readonly ILogger<BlueBubblesChannel> _logger;

    private readonly string _password;

    /// <summary>Tracks the timestamp (ms) of the last seen message to avoid reprocessing.</summary>
    private long _afterTimestampMs;

    public override ChannelName Name => ChannelName.BlueBubbles;

    protected override ILogger Logger => _logger;

    protected override JsonTypeInfo<BlueBubblesSendRequest> SendRequestTypeInfo => BlueBubblesJsonContext.Default.BlueBubblesSendRequest;

    public BlueBubblesChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<BlueBubblesChannel> logger,
        IHttpClientFactory httpClientFactory,
        ApprovedSendersStore approvedSenders)
        : base(options, bus, httpClientFactory, approvedSenders, "bluebubbles",
            ChannelName.BlueBubbles.Value,
            bridgeConfigCheck: static cfg => cfg.BridgeUrl is not null && cfg.Password is not null)
    {
        _logger = logger;
        _password = ChannelCfg?.Password ?? "";
        _afterTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    protected override Task OnBeforePollLoopAsync(CancellationToken ct)
    {
        // SECURITY NOTE: BlueBubbles API requires authentication via query parameter (?password=...).
        // This is an upstream API constraint — the server does not support Authorization headers.
        // The password will appear in HTTP access logs, proxy logs, and diagnostic traces.
        // Mitigation: always use HTTPS and restrict the bridge to a trusted local network.
        if (BridgeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            LogNonHttpsBridgeUrl(_logger, BridgeUrl);
        }

        return Task.CompletedTask;
    }

    protected override string GetPollUrl()
    {
        // Password in query string: required by BlueBubbles API (no header auth support).
        // SECURITY: Never log this URL — it contains the API password.
        return $"api/v1/message?password={Uri.EscapeDataString(_password)}&limit=50&sort=DESC&after={_afterTimestampMs}";
    }

    protected override async ValueTask<IReadOnlyList<BlueBubblesMessage>?> DeserializePollResponseAsync(
        Stream responseStream, CancellationToken ct)
    {
        var result = await JsonSerializer.DeserializeAsync(
            responseStream, BlueBubblesJsonContext.Default.BlueBubblesMessageResponse, ct).ConfigureAwait(false);
        return result?.Data;
    }

    protected override string? GetSenderId(BlueBubblesMessage item)
    {
        if (item.IsFromMe)
        {
            return null;
        }

        // Update the high-water mark for every message (including blocked senders)
        // to avoid re-fetching the same messages on next poll.
        if (item.DateCreated > _afterTimestampMs)
        {
            _afterTimestampMs = item.DateCreated;
        }

        var sender = item.Handle?.Address ?? "";
        return string.IsNullOrEmpty(sender) ? null : sender;
    }

    protected override ValueTask<InboundMessage?> MapIncomingAsync(
        BlueBubblesMessage item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Text))
        {
            return new ValueTask<InboundMessage?>((InboundMessage?)null);
        }

        var sender = item.Handle?.Address ?? "";

        return new ValueTask<InboundMessage?>(new InboundMessage(
            Channel: Name,
            SenderId: sender,
            SenderName: sender,
            Text: item.Text
        ));
    }

    protected override string GetSendUrl(OutboundMessage message)
    {
        // Password in query string: required by BlueBubbles API (no header auth support).
        // SECURITY: Never log this URL — it contains the API password.
        return $"api/v1/message/text?password={Uri.EscapeDataString(_password)}";
    }

    protected override BlueBubblesSendRequest MapToSendRequest(OutboundMessage message)
    {
        // Build chatGuid: iMessage;-;{recipientId}
        var chatGuid = $"iMessage;-;{message.RecipientId}";

        return new BlueBubblesSendRequest
        {
            ChatGuid = chatGuid,
            Message = message.Text
        };
    }

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message =
            "BlueBubbles bridge URL {BridgeUrl} uses plain HTTP — the API password will be transmitted in cleartext. Use HTTPS or restrict access to a trusted local network.")]
    private static partial void LogNonHttpsBridgeUrl(ILogger logger, string bridgeUrl);
}