using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Channels;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Core.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Channels.WhatsApp;

/// <summary>
/// Channel that communicates with a whatsapp-web.js REST bridge
/// (e.g. https://github.com/chrishubert/whatsapp-api).
/// Polls GET /api/messages/unread for inbound messages and
/// sends via POST /api/sendMessage.
/// </summary>
internal sealed partial class WhatsAppChannel : BridgePollingChannelBase<WhatsAppIncomingMessage, WhatsAppSendRequest>
{
    private readonly ILogger<WhatsAppChannel> _logger;

    private readonly VoiceTranscriptionService _voiceService;

    public override ChannelName Name => ChannelName.WhatsApp;

    protected override ILogger Logger => _logger;

    protected override JsonTypeInfo<WhatsAppSendRequest> SendRequestTypeInfo => WhatsAppJsonContext.Default.WhatsAppSendRequest;

    public WhatsAppChannel(
        IOptions<AppConfig> options,
        IMessageBus bus,
        ILogger<WhatsAppChannel> logger,
        IHttpClientFactory httpClientFactory,
        VoiceTranscriptionService voiceService,
        ApprovedSendersStore approvedSenders)
        : base(options, bus, httpClientFactory, approvedSenders, "whatsapp", ChannelName.WhatsApp.Value)
    {
        _logger = logger;
        _voiceService = voiceService;
    }

    protected override string GetPollUrl() => "api/messages/unread";

    protected override async ValueTask<IReadOnlyList<WhatsAppIncomingMessage>?> DeserializePollResponseAsync(
        Stream responseStream, CancellationToken ct)
    {
        return await JsonSerializer.DeserializeAsync(
            responseStream, WhatsAppJsonContext.Default.ListWhatsAppIncomingMessage, ct).ConfigureAwait(false);
    }

    protected override string? GetSenderId(WhatsAppIncomingMessage item)
    {
        if (item.FromMe)
        {
            return null;
        }

        return string.IsNullOrEmpty(item.From) ? null : item.From;
    }

    protected override async ValueTask<InboundMessage?> MapIncomingAsync(
        WhatsAppIncomingMessage item, CancellationToken ct)
    {
        var sender = item.From;

        // Voice note (ptt) or audio attachment — transcribe before publishing
        if (item.HasMedia && (
                string.Equals(item.Type, "ptt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Type, "audio", StringComparison.OrdinalIgnoreCase)))
        {
            var transcript = await TranscribeWhatsAppAudioAsync(item.Id, ct).ConfigureAwait(false);
            if (transcript is null)
            {
                return null;
            }

            return new InboundMessage(
                Channel: Name,
                SenderId: sender,
                SenderName: item.SenderName ?? sender,
                Text: transcript
            );
        }

        if (string.IsNullOrWhiteSpace(item.Body))
        {
            return null;
        }

        return new InboundMessage(
            Channel: Name,
            SenderId: sender,
            SenderName: item.SenderName ?? sender,
            Text: item.Body
        );
    }

    protected override string GetSendUrl(OutboundMessage message) => "api/sendMessage";

    protected override WhatsAppSendRequest MapToSendRequest(OutboundMessage message)
    {
        // WhatsApp chatIds typically end in @c.us for individual chats.
        var chatId = message.RecipientId;
        if (!chatId.Contains('@'))
        {
            chatId = $"{chatId}@c.us";
        }

        return new WhatsAppSendRequest
        {
            ChatId = chatId,
            Message = message.Text
        };
    }

    private async Task<string?> TranscribeWhatsAppAudioAsync(string messageId, CancellationToken ct)
    {
        if (!_voiceService.IsEnabled)
        {
            return "[Voice message — transcription unavailable]";
        }

        try
        {
            using var resp = await Http.GetAsync(
                $"api/downloadMedia/{Uri.EscapeDataString(messageId)}", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var media = await JsonSerializer.DeserializeAsync(
                stream, WhatsAppJsonContext.Default.WhatsAppMediaResponse, ct).ConfigureAwait(false);
            if (media?.Data is null)
            {
                return null;
            }

            var bytes = Convert.FromBase64String(media.Data);
            var mime = media.Mimetype?.Split(';')[0].Trim() ?? "audio/ogg";
            var text = await _voiceService.TranscribeAsync(bytes, mime, ct).ConfigureAwait(false);
            return text is not null ? $"[Voice] {text}" : null;
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
}