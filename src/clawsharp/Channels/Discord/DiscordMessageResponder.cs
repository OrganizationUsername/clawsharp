using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Core.Transcription;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Gateway.Responders;
using Remora.Rest.Core;
using Remora.Results;

namespace Clawsharp.Channels.Discord;

/// <summary>
///     Bridges Remora IMessageCreate events into the clawsharp IMessageBus.
///     Filters bots, applies the allow-list, and strips mention/prefix markers
///     before publishing so the agent loop sees clean plain-text input.
///     Also downloads image attachments (≤ 5 MB, from Discord CDN only) and includes
///     them in the InboundMessage for vision-capable LLM providers.
/// </summary>
public sealed partial class DiscordMessageResponder(
    IMessageBus bus,
    DiscordBotState botState,
    DiscordChannelOptions options,
    PairingStore pairingStore,
    ApprovedSendersStore approvedSenders,
    IDiscordRestChannelAPI restChannel,
    ILogger<DiscordMessageResponder> logger,
    IHttpClientFactory httpClientFactory,
    VoiceTranscriptionService voiceService) : IResponder<IMessageCreate>
{
    // MED-29: Use shared constant instead of local duplicate
    private const int MaxImageBytes = ClawsharpConstants.MaxImageBytes;

    private readonly AllowListPolicy _allowPolicy = options.AllowPolicy;

    private readonly bool _guildAllowAll = options.GuildAllowAll;

    private readonly IReadOnlySet<string>? _guildAllowList = options.GuildAllowList;

    private readonly string? _dmPolicy = options.DmPolicy;

    private readonly bool _groupPolicyOpen = string.Equals(options.GroupPolicy, "open", StringComparison.OrdinalIgnoreCase);

    private readonly HttpClient _http = httpClientFactory.CreateClient("discord");

    public async Task<Result> RespondAsync(IMessageCreate ev, CancellationToken ct = default)
    {
        if (ShouldIgnoreEvent(ev))
        {
            return Result.FromSuccess();
        }

        // Guild allowlist check — reject events from unlisted servers
        var isDm = !ev.GuildID.TryGet(out var guildId);
        if (!isDm && !_guildAllowAll && (_guildAllowList is null || !_guildAllowList.Contains(guildId.ToString())))
        {
            return Result.FromSuccess();
        }

        var authorId = ev.Author.ID.ToString();
        if (!await CheckUserAllowedAsync(authorId, isDm, ev.ChannelID, ev.Author.Username, ct))
        {
            return Result.FromSuccess();
        }

        var guildResult = ExtractGuildText(ev, botState.SelfId);
        if (guildResult is null)
        {
            return Result.FromSuccess();
        }

        var (text, hasAttachments) = guildResult.Value;

        // For guild messages, allow image-only (no text after stripping prefix/mention)
        // as long as there are attachments to process.
        if (string.IsNullOrWhiteSpace(text) && !hasAttachments)
        {
            return Result.FromSuccess();
        }

        var images = await DownloadImageAttachmentsAsync(ev.Attachments, ct);
        var transcript = await TranscribeVoiceAttachmentAsync(ev.Attachments, ct);
        if (transcript is not null)
        {
            text = string.IsNullOrWhiteSpace(text)
                ? $"[Voice] {transcript}"
                : $"{text}\n[Voice] {transcript}";
        }

        // Ensure there's something to send after all attachment processing
        if (string.IsNullOrWhiteSpace(text) && (images is null || images.Count == 0))
        {
            return Result.FromSuccess();
        }

        await bus.PublishAsync(new InboundMessage(
            Channel: ChannelName.Discord,
            SenderId: ev.ChannelID.ToString(),
            SenderName: ev.Author.Username,
            Text: text,
            Images: images is { Count: > 0 } ? images : null
        ), ct);

        return Result.FromSuccess();
    }

    /// <summary>
    ///     Returns true if the event should be ignored (bot, system, or empty with no attachments).
    /// </summary>
    private static bool ShouldIgnoreEvent(IMessageCreate ev)
    {
        if (ev.Author.IsBot.TryGet(out var isBot) && isBot)
        {
            return true;
        }

        if (ev.Author.IsSystem.TryGet(out var isSystem) && isSystem)
        {
            return true;
        }

        // Skip if no text and no attachments
        if (string.IsNullOrWhiteSpace(ev.Content) && ev.Attachments.Count == 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Checks whether the user is allowed via the static allowlist or dynamic approved senders.
    ///     If the user is not allowed and DM policy is "pairing", sends a pairing code as a side effect.
    /// </summary>
    /// <returns>True if the user is allowed to interact with the bot.</returns>
    private async Task<bool> CheckUserAllowedAsync(
        string authorId, bool isDm, Snowflake channelId, string username, CancellationToken ct)
    {
        var isAllowed = _allowPolicy.IsAllowed(authorId)
                        || await approvedSenders.IsApprovedAsync("discord", authorId, ct);
        if (isAllowed)
        {
            return true;
        }

        if (isDm && string.Equals(_dmPolicy, "pairing", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var code = await pairingStore.GetOrCreateCodeAsync("discord", authorId, username, ct);
                var msg = $"Hi! To use this bot, send your operator the pairing code: **{code}**\n" +
                          "This code expires in 24 hours.";
                await restChannel.CreateMessageAsync(channelId, msg, ct: ct);
                LogPairingSent(authorId, code);
            }
            catch (Exception ex)
            {
                LogPairingFailed(ex);
            }
        }

        return false;
    }

    /// <summary>
    ///     Extracts and normalizes the message text for guild and DM contexts.
    ///     For DMs, returns the content as-is. For guilds, checks mention/prefix/voice triggers
    ///     based on group policy and strips the mention or prefix marker.
    /// </summary>
    /// <returns>
    ///     A tuple of (normalized text, hasAttachments), or null if the guild message should be ignored.
    /// </returns>
    private (string Text, bool HasAttachments)? ExtractGuildText(IMessageCreate ev, Snowflake? selfId)
    {
        var content = ev.Content;
        var hasAttachments = ev.Attachments.Count > 0;
        var isDm = !ev.GuildID.HasValue;

        if (isDm)
        {
            return (content, hasAttachments);
        }

        // In guilds: when groupPolicy is "open", respond to all messages.
        // Otherwise (default "mention"), only respond when @mentioned, prefixed with '!', or voice attachment.
        var mentioned = selfId.HasValue && ev.Mentions.Any(m => m.ID == selfId.Value);
        var prefixed = content.StartsWith('!');
        // MED-27: Use shared constants for Discord CDN URLs
        var hasVoice = ev.Attachments.Any(a =>
            a.ContentType.TryGet(out var voiceMime) &&
            (voiceMime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
             || voiceMime.StartsWith("application/ogg", StringComparison.OrdinalIgnoreCase)
             || voiceMime.StartsWith("video/mp4", StringComparison.OrdinalIgnoreCase)) &&
            (a.Url.StartsWith(ClawsharpConstants.DiscordCdnHost, StringComparison.Ordinal) ||
             a.Url.StartsWith(ClawsharpConstants.DiscordMediaHost, StringComparison.Ordinal)));

        if (!_groupPolicyOpen && !mentioned && !prefixed && !hasVoice)
        {
            return null;
        }

        var text = content;
        if (selfId.HasValue)
        {
            text = text.Replace($"<@{selfId.Value}>", "", StringComparison.Ordinal).Trim();
        }

        if (text.StartsWith('!'))
        {
            text = text[1..].Trim();
        }

        return (text, hasAttachments);
    }

    /// <summary>
    ///     Downloads image attachments from Discord CDN (best-effort, max 5 MB each).
    ///     Filters by MIME type, size, and CDN origin. Applies SSRF guard as defense-in-depth.
    /// </summary>
    /// <returns>A list of image attachments, or null if none were downloaded.</returns>
    private async Task<List<ImageAttachment>?> DownloadImageAttachmentsAsync(
        IReadOnlyList<IAttachment> attachments, CancellationToken ct)
    {
        List<ImageAttachment>? images = null;
        foreach (var attachment in attachments)
        {
            if (!attachment.ContentType.TryGet(out var mimeType))
            {
                continue;
            }

            if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (attachment.Size > MaxImageBytes)
            {
                continue;
            }

            // Only download from official Discord CDN to prevent SSRF
            var url = attachment.Url;
            if (!url.StartsWith(ClawsharpConstants.DiscordCdnHost, StringComparison.Ordinal) &&
                !url.StartsWith(ClawsharpConstants.DiscordMediaHost, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                // Defense-in-depth: SSRF guard even after CDN whitelist (CDN domains could resolve to private IPs)
                var ssrfResult = await SsrfGuard.CheckAsync(new Uri(url), ct);
                if (ssrfResult is not null)
                {
                    LogSsrfBlocked(url, ssrfResult);
                    continue;
                }

                var bytes = await _http.GetByteArrayAsync(url, ct);
                if (bytes.Length > MaxImageBytes)
                {
                    continue;
                }

                // Use the content-type MIME type, stripping any parameters (e.g. "; charset=utf-8")
                var mime = mimeType.Split(';')[0].Trim();
                images ??= [];
                images.Add(ImageAttachment.Create(Convert.ToBase64String(bytes), mime));
            }
            catch
            {
                // Best-effort: skip attachments that fail to download
            }
        }

        return images;
    }

    /// <summary>
    ///     Transcribes the first audio/voice attachment from Discord CDN (best-effort).
    ///     Filters by audio MIME types and CDN origin. Applies SSRF guard as defense-in-depth.
    /// </summary>
    /// <returns>The transcript text, or null if no voice attachment was transcribed.</returns>
    private async Task<string?> TranscribeVoiceAttachmentAsync(
        IReadOnlyList<IAttachment> attachments, CancellationToken ct)
    {
        if (!voiceService.IsEnabled)
        {
            return null;
        }

        foreach (var attachment in attachments)
        {
            if (!attachment.ContentType.TryGet(out var mimeType))
            {
                continue;
            }

            // Discord voice messages may arrive as audio/*, application/ogg, or video/mp4
            var isAudio = mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                          || mimeType.StartsWith("application/ogg", StringComparison.OrdinalIgnoreCase)
                          || mimeType.StartsWith("video/mp4", StringComparison.OrdinalIgnoreCase);
            if (!isAudio)
            {
                continue;
            }

            var url = attachment.Url;
            if (!url.StartsWith(ClawsharpConstants.DiscordCdnHost, StringComparison.Ordinal) &&
                !url.StartsWith(ClawsharpConstants.DiscordMediaHost, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                // Defense-in-depth: SSRF guard on audio downloads too
                var ssrfResult = await SsrfGuard.CheckAsync(new Uri(url), ct);
                if (ssrfResult is not null)
                {
                    LogSsrfBlocked(url, ssrfResult);
                    continue;
                }

                var bytes = await _http.GetByteArrayAsync(url, ct);
                var mime = mimeType.Split(';')[0].Trim();
                var transcript = await voiceService.TranscribeAsync(bytes, mime, ct);
                if (transcript is not null)
                {
                    return transcript; // one voice message per event
                }
            }
            catch
            {
                // Best-effort: skip attachments that fail to transcribe
            }
        }

        return null;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Sent pairing code to Discord user {UserId}: {Code}")]
    private partial void LogPairingSent(string userId, string code);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Failed to send Discord pairing code")]
    private partial void LogPairingFailed(Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "SSRF blocked Discord attachment download: {Url} — {Reason}")]
    private partial void LogSsrfBlocked(string url, string reason);
}