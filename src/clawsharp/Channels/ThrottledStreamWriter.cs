using System.Diagnostics;
using System.Text;

namespace Clawsharp.Channels;

/// <summary>
/// Encapsulates the accumulate-and-throttled-edit streaming pattern used by multiple channels.
/// Accepts an <see cref="IAsyncEnumerable{T}"/> of string tokens and drives two delegates: one to send the initial
/// placeholder message and one to edit it with accumulated text at a configurable interval.
/// </summary>
public static class ThrottledStreamWriter
{
    /// <summary>Default throttle interval between edits (500 ms).</summary>
    private static readonly TimeSpan DefaultThrottleInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Default typing cursor appended to in-progress text.</summary>
    private const string DefaultCursorChar = "\u258b"; // ▋

    /// <summary>
    /// Drives a streaming response through a channel's send/edit delegates.
    /// </summary>
    /// <param name="tokens">The token stream from the LLM provider (plain text deltas).</param>
    /// <param name="sendInitialAsync">
    /// Called once with the cursor char to create the initial placeholder message.
    /// Returns a message ID (e.g. Slack ts, Matrix event_id, Mattermost post ID) or null if the send failed.
    /// </param>
    /// <param name="editMessageAsync">
    /// Called periodically with (messageId, accumulatedText) to update the placeholder.
    /// Implementations should swallow exceptions internally (best-effort edits).
    /// </param>
    /// <param name="throttleInterval">How often to edit. Default 500 ms.</param>
    /// <param name="cursorChar">Appended to in-progress text. Default "\u258b" (▋).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fully accumulated text, or "(no response)" if nothing was received.</returns>
    public static async Task<string> WriteAsync(
        IAsyncEnumerable<string> tokens,
        Func<string, CancellationToken, Task<string?>> sendInitialAsync,
        Func<string, string, CancellationToken, Task> editMessageAsync,
        TimeSpan? throttleInterval = null,
        string cursorChar = DefaultCursorChar,
        CancellationToken ct = default)
    {
        var result = await WriteWithResultAsync(tokens, sendInitialAsync, editMessageAsync,
            throttleInterval, cursorChar, ct).ConfigureAwait(false);
        return result.Text;
    }

    /// <summary>
    /// Same as <see cref="WriteAsync"/> but returns a <see cref="StreamResult"/> that includes
    /// the placeholder message ID so the caller can distinguish placeholder-created vs fallback paths.
    /// </summary>
    public static async Task<StreamResult> WriteWithResultAsync(
        IAsyncEnumerable<string> tokens,
        Func<string, CancellationToken, Task<string?>> sendInitialAsync,
        Func<string, string, CancellationToken, Task> editMessageAsync,
        TimeSpan? throttleInterval = null,
        string cursorChar = DefaultCursorChar,
        CancellationToken ct = default)
    {
        var interval = throttleInterval ?? DefaultThrottleInterval;

        string? messageId = null;
        try
        {
            messageId = await sendInitialAsync(cursorChar, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort
        }

        var textBuilder = new StringBuilder();
        var lastUpdate = Stopwatch.GetTimestamp();

        await foreach (var token in tokens.WithCancellation(ct).ConfigureAwait(false))
        {
            textBuilder.Append(token);

            if (messageId is not null)
            {
                var elapsed = Stopwatch.GetElapsedTime(lastUpdate);
                if (elapsed >= interval)
                {
                    lastUpdate = Stopwatch.GetTimestamp();
                    try
                    {
                        await editMessageAsync(messageId, textBuilder.ToString() + cursorChar, ct)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort
                    }
                }
            }
        }

        var finalText = "(no response)";
        if (textBuilder.Length > 0)
        {
            finalText = textBuilder.ToString();
        }

        if (messageId is not null)
        {
            try
            {
                await editMessageAsync(messageId, finalText, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort
            }
        }

        return new StreamResult(finalText, messageId);
    }

    /// <summary>Result of a throttled stream write, including the accumulated text and the placeholder message ID.</summary>
    /// <param name="Text">The fully accumulated response text.</param>
    /// <param name="MessageId">The placeholder message ID, or null if the initial send failed.</param>
    public readonly record struct StreamResult(string Text, string? MessageId)
    {
        /// <summary>Whether the initial placeholder was successfully created.</summary>
        public bool PlaceholderCreated => MessageId is not null;
    }
}