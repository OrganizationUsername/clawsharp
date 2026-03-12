using System.Diagnostics;
using System.Text;
using Clawsharp.Core;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;
using Remora.Results;

namespace Clawsharp.Channels.Discord;

/// <summary>
///     IChannel adapter for Discord using Remora.Discord's REST API.
///     The Gateway (receiving messages) is managed by Remora as a hosted service.
///     <see cref="SendAsync" /> uses IDiscordRestChannelAPI to post replies.
/// </summary>
public sealed partial class DiscordChannel(
    IDiscordRestChannelAPI channelAPI,
    ILogger<DiscordChannel> logger) : IChannel, IStreamingChannel, IThinkingIndicator
{
    // MED-28: Extracted from magic number; matches Discord API limit.
    private const int MaxMessageLength = ClawsharpConstants.DiscordMaxMessageLength;

    private static readonly TimeSpan EditInterval = TimeSpan.FromMilliseconds(300);

    public ChannelName Name => ChannelName.Discord;

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!ulong.TryParse(message.RecipientId, out var rawId))
        {
            LogInvalidChannelId(logger, message.RecipientId);
            return;
        }

        var channelId = new Snowflake(rawId);

        foreach (var chunk in MessageChunker.Split(message.Text, MaxMessageLength))
        {
            var result = await channelAPI.CreateMessageAsync(channelId, chunk, ct: ct);
            if (!result.IsSuccess)
            {
                LogSendError(logger, result.Error);
            }
        }
    }

    /// <summary>
    ///     Discord does not support true streaming, so this method posts a "..." placeholder then
    ///     edits it as tokens accumulate — at most once per 300 ms — to stay within rate limits.
    ///     A final edit delivers the complete text.
    /// </summary>
    /// <remarks>
    ///     Not refactored to <see cref="ThrottledStreamWriter"/> because Remora's typed API
    ///     (Snowflake IDs, Result&lt;T&gt; returns, 300 ms interval, overflow chunking with delete)
    ///     does not map cleanly to the string-based delegates. See ThrottledStreamWriter for the
    ///     shared pattern used by Slack, Matrix, Mattermost, and Telegram.
    /// </remarks>
    public async Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        if (!ulong.TryParse(message.RecipientId, out var rawId))
        {
            LogInvalidChannelIdForStreaming(logger, message.RecipientId);
            return;
        }

        var channelId = new Snowflake(rawId);

        // Post a placeholder so the user sees activity immediately.
        var placeholderResult = await channelAPI.CreateMessageAsync(channelId, "...", ct: ct);
        if (!placeholderResult.IsSuccess)
        {
            await FallbackConsumeAndSendAsync(message, placeholderResult.Error, tokens, ct);
            return;
        }

        var messageId = placeholderResult.Entity.ID;
        var sb = new StringBuilder();
        var lastEditTimestamp = Stopwatch.GetTimestamp();

        await foreach (var token in tokens.WithCancellation(ct))
        {
            sb.Append(token);

            if (Stopwatch.GetElapsedTime(lastEditTimestamp) >= EditInterval)
            {
                var current = sb.ToString();
                var editContent = current.Length > MaxMessageLength ? current[..MaxMessageLength] : current;
                var editResult = await channelAPI.EditMessageAsync(channelId, messageId, editContent, ct: ct);
                if (!editResult.IsSuccess)
                {
                    LogStreamEditError(logger, editResult.Error);
                }

                lastEditTimestamp = Stopwatch.GetTimestamp();
            }
        }

        // Final edit with the complete accumulated text.
        var finalText = sb.ToString();
        if (finalText.Length > 0)
        {
            await DeliverFinalStreamTextAsync(channelId, messageId, finalText, ct);
        }
    }

    /// <summary>
    ///     HIGH-07: Placeholder failed — consumes all tokens then sends via <see cref="SendAsync"/>.
    /// </summary>
    private async Task FallbackConsumeAndSendAsync(
        OutboundMessage message, IResultError? error, IAsyncEnumerable<string> tokens, CancellationToken ct)
    {
        LogStreamPlaceholderSendError(logger, error);
        var fallbackSb = new StringBuilder();
        await foreach (var t in tokens.WithCancellation(ct))
        {
            fallbackSb.Append(t);
        }

        var fallbackText = fallbackSb.Length > 0 ? fallbackSb.ToString() : "(no response)";
        await SendAsync(message with { Text = fallbackText }, ct);
    }

    /// <summary>
    ///     Delivers the final accumulated stream text. If the text exceeds the Discord message limit,
    ///     deletes the placeholder and sends chunked messages instead.
    /// </summary>
    private async Task DeliverFinalStreamTextAsync(
        Snowflake channelId, Snowflake messageId, string finalText, CancellationToken ct)
    {
        if (finalText.Length > MaxMessageLength)
        {
            await channelAPI.DeleteMessageAsync(channelId, messageId, ct: ct);
            foreach (var chunk in MessageChunker.Split(finalText, MaxMessageLength))
            {
                var r = await channelAPI.CreateMessageAsync(channelId, chunk, ct: ct);
                if (!r.IsSuccess)
                {
                    LogStreamOverflowSendError(logger, r.Error);
                }
            }
        }
        else
        {
            var finalResult = await channelAPI.EditMessageAsync(channelId, messageId, finalText, ct: ct);
            if (!finalResult.IsSuccess)
            {
                LogStreamFinalEditError(logger, finalResult.Error);
            }
        }
    }

    /// <inheritdoc />
    public async Task StartThinkingAsync(string recipientId, CancellationToken ct = default)
    {
        try
        {
            if (!ulong.TryParse(recipientId, out var rawId))
            {
                return;
            }

            var channelId = new Snowflake(rawId);
            var result = await channelAPI.TriggerTypingIndicatorAsync(channelId, ct);
            if (!result.IsSuccess)
            {
                LogTypingIndicatorError(logger, result.Error);
            }
        }
        catch
        {
            /* best-effort — never throw into caller */
        }
    }

    /// <inheritdoc />
    public Task StopThinkingAsync(string recipientId, CancellationToken ct = default)
    {
        // Discord typing indicators expire automatically after ~10 seconds.
        // No explicit stop action is available in the API.
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Sends a file attachment to a Discord channel via the REST API.
    ///     Uses Remora's <c>CreateMessageAsync</c> with <see cref="FileData"/> for multipart upload.
    /// </summary>
    /// <param name="channelId">Target channel snowflake ID.</param>
    /// <param name="filename">Filename shown in Discord (e.g. "report.pdf").</param>
    /// <param name="content">Stream containing the file bytes. Ownership is transferred to Remora (disposed after call).</param>
    /// <param name="messageText">Optional message text sent alongside the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the file was sent successfully.</returns>
    public async Task<bool> SendFileAsync(Snowflake channelId, string filename, Stream content, string? messageText, CancellationToken ct)
    {
        // Safe default: 8 MB (works for all servers regardless of Nitro/boost level)
        const long maxFileSizeBytes = 8 * 1024 * 1024;

        if (content.CanSeek && content.Length > maxFileSizeBytes)
        {
            LogFileTooLarge(logger, filename, content.Length, maxFileSizeBytes);
            return false;
        }

        var fileData = new FileData(filename, content, filename);
        var attachments = new List<OneOf<FileData, IPartialAttachment>> { fileData };

        var result = await channelAPI.CreateMessageAsync(
            channelId,
            content: messageText ?? default(Optional<string>),
            attachments: new Optional<IReadOnlyList<OneOf<FileData, IPartialAttachment>>>(attachments),
            ct: ct);

        if (!result.IsSuccess)
        {
            LogSendFileError(logger, filename, result.Error);
            return false;
        }

        return true;
    }

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "Typing indicator error: {Error}")]
    private static partial void LogTypingIndicatorError(ILogger logger, IResultError? error);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Invalid channel ID: {RecipientId}")]
    private static partial void LogInvalidChannelId(ILogger logger, string recipientId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Send error: {Error}")]
    private static partial void LogSendError(ILogger logger, IResultError? error);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Invalid channel ID for streaming: {RecipientId}")]
    private static partial void LogInvalidChannelIdForStreaming(ILogger logger, string recipientId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Stream placeholder send error: {Error}")]
    private static partial void LogStreamPlaceholderSendError(ILogger logger, IResultError? error);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Stream edit error: {Error}")]
    private static partial void LogStreamEditError(ILogger logger, IResultError? error);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Stream overflow send error: {Error}")]
    private static partial void LogStreamOverflowSendError(ILogger logger, IResultError? error);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Stream final edit error: {Error}")]
    private static partial void LogStreamFinalEditError(ILogger logger, IResultError? error);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "File '{Filename}' too large ({Size} bytes, max {MaxSize} bytes)")]
    private static partial void LogFileTooLarge(ILogger logger, string filename, long size, long maxSize);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "Send file '{Filename}' error: {Error}")]
    private static partial void LogSendFileError(ILogger logger, string filename, IResultError? error);
}