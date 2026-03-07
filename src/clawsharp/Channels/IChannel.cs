using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Channels;

public interface IChannel
{
    ChannelName Name { get; }

    Task SendAsync(OutboundMessage message, CancellationToken ct = default);
}

/// <summary>
///     Optional extension of <see cref="IChannel" /> that supports token-by-token streaming.
///     When both the provider and the active channel implement this interface, AgentLoop
///     delivers text deltas inline instead of buffering the full response.
/// </summary>
public interface IStreamingChannel : IChannel
{
    /// <summary>
    ///     Stream text tokens to the recipient as they arrive.
    ///     Implementations must write each token without waiting for the full response.
    /// </summary>
    Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default);
}

/// <summary>
///     Optional extension of <see cref="IChannel" /> that supports native file uploads.
///     Channels implementing this interface can deliver files directly to the user
///     (e.g. Slack 3-step upload, Discord multipart attachment).
/// </summary>
public interface IFileChannel : IChannel
{
    /// <summary>
    ///     Sends a file to the recipient.
    /// </summary>
    /// <param name="recipientId">Target channel/user identifier.</param>
    /// <param name="filename">Display filename (e.g. "report.pdf").</param>
    /// <param name="content">Raw file bytes.</param>
    /// <param name="message">Optional message to accompany the file.</param>
    /// <param name="threadId">Optional thread identifier for threaded replies.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the file was delivered successfully.</returns>
    Task<bool> SendFileAsync(string recipientId, string filename, ReadOnlyMemory<byte> content,
                             string? message, string? threadId, CancellationToken ct);
}