using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Clawsharp.Channels;

/// <summary>
/// Shared WebSocket receive-loop utility. Reads complete text frames from a
/// <see cref="ClientWebSocket"/> and yields them as strings. Handles buffer
/// management, multi-frame messages, size limits, and close detection.
/// </summary>
internal static class WebSocketReceiver
{
    private const int DefaultBufferSize = 65_536;

    private const int DefaultMaxMessageBytes = 1 * 1024 * 1024; // 1 MB

    /// <summary>
    /// Reads complete text messages from the WebSocket until the connection closes
    /// or the <paramref name="ct"/> is cancelled. Yields each complete message as a string.
    /// Returns (terminates the enumerable) on WebSocket close frame.
    /// </summary>
    /// <param name="ws">The connected <see cref="ClientWebSocket"/> to read from.</param>
    /// <param name="maxMessageBytes">Maximum allowed message size in bytes before throwing.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async IAsyncEnumerable<string> ReceiveMessagesAsync(
        ClientWebSocket ws,
        int maxMessageBytes = DefaultMaxMessageBytes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > maxMessageBytes)
                    {
                        throw new InvalidOperationException(
                            $"WebSocket message exceeded {maxMessageBytes} byte limit");
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                yield return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Sends a graceful close frame to the server. Swallows exceptions if the
    /// connection is already broken.
    /// </summary>
    public static async Task CloseGracefullyAsync(ClientWebSocket ws)
    {
        if (ws.State == WebSocketState.Open)
        {
            try
            {
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default).ConfigureAwait(false);
            }
            catch
            {
                // Connection may already be broken
            }
        }
    }
}