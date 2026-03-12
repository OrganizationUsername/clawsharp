using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;

namespace Clawsharp.Channels.Nostr;

/// <summary>
/// Nostr protocol channel. Receives DMs via NIP-04 (kind 4) and NIP-17 gift-wrap (kind 1059),
/// connects to multiple relays using NNostr.Client, and replies using the configured DM protocol.
/// </summary>
/// <remarks>
/// TODO: If NativeAOT is re-enabled, NNostr.Client's receive path uses reflection-based
/// JsonElement.Deserialize&lt;NostrEvent&gt;(). Replace with manual JsonDocument traversal.
/// </remarks>
public sealed partial class NostrChannel : LifecycleBackgroundService, IChannel
{
    private readonly AllowListPolicy _allowPolicy = AllowListPolicy.AllowAll;

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly bool _enabled;

    private readonly IMessageBus _bus;

    private readonly ILogger<NostrChannel> _logger;

    private readonly string? _privKeyRaw;

    private readonly string[]? _relayUrls;

    private readonly bool _useNip17;

    private ECPrivKey? _privKey;

    private string? _pubKeyHex;

    private CompositeNostrClient? _client;

    private CancellationToken _stoppingToken;

    // Event deduplication: multiple relays may deliver the same event
    private readonly BoundedDeduplicator<string> _dedup = new(10_000, StringComparer.Ordinal);

    public ChannelName Name => ChannelName.Nostr;

    public NostrChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<NostrChannel> logger,
        ApprovedSendersStore approvedSenders)
    {
        _bus = bus;
        _logger = logger;
        _approvedSenders = approvedSenders;

        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Nostr.Value);
        if (cfg is not { Enabled: true } || cfg.NostrPrivKey is null)
        {
            _enabled = false;
            return;
        }

        _enabled = true;
        _privKeyRaw = cfg.NostrPrivKey;
        _relayUrls = cfg.NostrRelays;
        _useNip17 = cfg.NostrNip17;
        _allowPolicy = new AllowListPolicy(cfg.AllowFrom, StringComparer.Ordinal);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        _stoppingToken = stoppingToken;
        _privKey = LoadPrivKey(_privKeyRaw!);
        _pubKeyHex = _privKey.CreateXOnlyPubKey().ToHex();
        var npub = _privKey.CreateXOnlyPubKey().ToNIP19();
        LogBotPubKey(_pubKeyHex, npub);

        var backoffMs = 5_000;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
                backoffMs = 5_000; // reset on clean exit
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogConnectionError(backoffMs, ex);
                await Task.Delay(backoffMs, stoppingToken);
                backoffMs = Math.Min(backoffMs * 2, 60_000);
            }
        }
    }

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (_client is null || _privKey is null)
        {
            return;
        }

        var recipientPubKeyHex = message.RecipientId;

        if (_useNip17)
        {
            var pTag = new NostrEventTag { TagIdentifier = "p", Data = [recipientPubKeyHex] };
            var kind14 = new NostrEvent
            {
                Kind = 14,
                Content = message.Text,
                Tags = [pTag]
            };

            var recipientPubKey = NostrExtensions.ParsePubKey(Convert.FromHexString(recipientPubKeyHex));
            var giftWrap = await NIP17.Create(kind14, _privKey, recipientPubKey, [pTag]);
            await _client.PublishEvent(giftWrap, ct);
        }
        else
        {
            var reply = new NostrEvent
            {
                Kind = 4,
                Content = message.Text,
                Tags = [new NostrEventTag { TagIdentifier = "p", Data = [recipientPubKeyHex] }]
            };

            // handlenip4: true auto-encrypts content before signing
            await reply.ComputeIdAndSignAsync(_privKey, handlenip4: true);
            await _client.PublishEvent(reply, ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var relays = _relayUrls is { Length: > 0 }
            ? _relayUrls
            : ["wss://relay.damus.io", "wss://nos.lol"];

        var relayUris = relays.Select(r => new Uri(r)).ToArray();
        _client = new CompositeNostrClient(relayUris);

        _client.EventsReceived += OnEventsReceived;

        try
        {
            await _client.ConnectAndWaitUntilConnected(ct);
            LogConnected(relays.Length);

            // Subscribe to NIP-04 DMs (kind 4) and NIP-17 gift wraps (kind 1059)
            var subId = Guid.NewGuid().ToString("N")[..32];
            var filter = new NostrSubscriptionFilter
            {
                Kinds = [4, 1059],
                ReferencedPublicKeys = [_pubKeyHex!],
                Since = DateTimeOffset.UtcNow.AddSeconds(-5) // slight overlap for safety
            };
            await _client.CreateSubscription(subId, [filter], ct);

            // Keep alive until cancellation
            await Task.Delay(Timeout.Infinite, ct);
        }
        finally
        {
            _client.EventsReceived -= OnEventsReceived;
            _client.Dispose();
            _client = null;
        }
    }

    private void OnEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) args)
    {
        foreach (var ev in args.events)
        {
            _ = HandleEventAsync(ev, _stoppingToken).LogExceptions(_logger, "NostrChannel.HandleEvent");
        }
    }

    private async Task HandleEventAsync(NostrEvent ev, CancellationToken ct)
    {
        try
        {
            if (_privKey is null || ct.IsCancellationRequested)
            {
                return;
            }

            // Dedup: multiple relays may deliver the same event
            if (!IsNewEvent(ev.Id))
            {
                return;
            }

            var decrypted = await DecryptEventAsync(ev);
            if (decrypted is null)
            {
                return;
            }

            var (plaintext, senderPubKey) = decrypted.Value;

            if (string.IsNullOrWhiteSpace(plaintext))
            {
                return;
            }

            // AllowFrom policy check on sender pubkey
            if (!_allowPolicy.IsAllowed(senderPubKey) &&
                !await _approvedSenders.IsApprovedAsync(ChannelName.Nostr.Value, senderPubKey, ct))
            {
                LogBlockedSender(senderPubKey[..Math.Min(16, senderPubKey.Length)]);
                return;
            }

            // Use first 8 hex chars of pubkey as display name
            var senderName = senderPubKey.Length >= 8 ? senderPubKey[..8] : senderPubKey;

            await _bus.PublishAsync(new InboundMessage(
                Channel: Name,
                SenderId: senderPubKey,
                SenderName: senderName,
                Text: plaintext
            ), ct);
        }
        catch (OperationCanceledException)
        {
            /* shutdown */
        }
        catch (Exception ex)
        {
            LogEventHandleError(ev.Id ?? "unknown", ex);
        }
    }

    /// <summary>
    /// Decrypts a Nostr event based on its kind. Kind 4 uses NIP-04 legacy encryption;
    /// kind 1059 uses NIP-17 gift wrap. Returns <c>null</c> for unsupported event kinds.
    /// </summary>
    private async Task<(string Plaintext, string SenderPubKey)?> DecryptEventAsync(NostrEvent ev)
    {
        if (ev.Kind == 4)
        {
            // NIP-04 legacy encrypted DM
            var plaintext = await ev.DecryptNip04EventAsync(_privKey!);
            var senderPubKey = ev.PublicKey ?? "";
            return (plaintext, senderPubKey);
        }

        if (ev.Kind == 1059)
        {
            // NIP-17 gift wrap: unwrap -> seal -> kind 14 message
            var innerEvent = await NIP17.Open(ev, _privKey!);
            var plaintext = innerEvent.Content ?? "";
            // The inner event's PublicKey is the real sender (set from the seal layer)
            var senderPubKey = innerEvent.PublicKey ?? "";
            return (plaintext, senderPubKey);
        }

        return null;
    }

    /// <summary>
    /// Tracks event IDs for deduplication across relays.
    /// Returns <c>true</c> if this is a new event, <c>false</c> if already seen.
    /// </summary>
    private bool IsNewEvent(string? eventId)
    {
        if (eventId is null)
        {
            return false;
        }

        return _dedup.TryAdd(eventId);
    }

    /// <summary>
    /// Parses a Nostr private key from either nsec1 (bech32) or 64-char hex format.
    /// </summary>
    private static ECPrivKey LoadPrivKey(string value)
    {
        if (value.StartsWith("nsec1", StringComparison.Ordinal))
        {
            return value.FromNIP19Nsec();
        }

        var bytes = Convert.FromHexString(value);
        return ECPrivKey.Create(bytes);
    }

    // ── LoggerMessage partial methods ────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Bot pubkey: {PubKeyHex} (npub: {Npub})")]
    private partial void LogBotPubKey(string pubKeyHex, string npub);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Connected to {RelayCount} relay(s)")]
    private partial void LogConnected(int relayCount);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Relay connection error, reconnecting in {BackoffMs}ms")]
    private partial void LogConnectionError(int backoffMs, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Blocked sender {SenderPrefix}")]
    private partial void LogBlockedSender(string senderPrefix);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Failed to handle event {EventId}")]
    private partial void LogEventHandleError(string eventId, Exception exception);
}