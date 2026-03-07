using System.Runtime.CompilerServices;
using System.Text;

namespace Clawsharp.Providers.Bedrock;

/// <summary>
///     Parser for the AWS binary event-stream format used by Bedrock ConverseStream responses.
///     Each message consists of a 12-byte prelude (total length, headers length, prelude CRC),
///     followed by headers, a JSON payload, and a 4-byte message CRC.
/// </summary>
internal static class BedrockStreamParser
{
    /// <summary>
    ///     Parse binary event-stream messages from a Bedrock ConverseStream response stream,
    ///     yielding the event type and raw JSON payload for each message.
    /// </summary>
    public static async IAsyncEnumerable<(string EventType, ReadOnlyMemory<byte> Payload)> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prelude = new byte[12];

        while (!ct.IsCancellationRequested)
        {
            // Read 12-byte prelude: total_length(4) + headers_length(4) + prelude_crc(4)
            if (!await ReadExactAsync(stream, prelude, 0, 12, ct))
            {
                yield break;
            }

            var totalLength = ReadBigEndianUInt32(prelude, 0);
            var headersLength = ReadBigEndianUInt32(prelude, 4);
            // Bytes 8-11 are the prelude CRC — skip (TLS protects integrity)

            // Read the remainder: headers + payload + 4-byte message CRC
            var remaining = (int)totalLength - 12;
            if (remaining <= 0)
            {
                continue;
            }

            var messageBytes = new byte[remaining];
            if (!await ReadExactAsync(stream, messageBytes, 0, remaining, ct))
            {
                yield break;
            }

            // Parse headers to extract :event-type or :exception-type
            var eventType = ParseEventType(messageBytes.AsSpan(0, (int)headersLength));
            if (eventType is null)
            {
                continue;
            }

            // Extract JSON payload (between headers and 4-byte trailing message CRC)
            var payloadStart = (int)headersLength;
            var payloadLength = remaining - payloadStart - 4;
            if (payloadLength <= 0)
            {
                continue;
            }

            var payload = new byte[payloadLength];
            Buffer.BlockCopy(messageBytes, payloadStart, payload, 0, payloadLength);

            yield return (eventType, payload);
        }
    }

    /// <summary>
    ///     Scan headers for the <c>:event-type</c> or <c>:exception-type</c> key.
    ///     Header format: [1-byte key length][key][1-byte value type][2-byte value length][value].
    ///     Value type 7 = UTF-8 string.
    /// </summary>
    private static string? ParseEventType(ReadOnlySpan<byte> headers)
    {
        var pos = 0;
        while (pos < headers.Length)
        {
            var keyLen = headers[pos++];
            if (pos + keyLen > headers.Length)
            {
                break;
            }

            var key = Encoding.UTF8.GetString(headers.Slice(pos, keyLen));
            pos += keyLen;

            if (pos >= headers.Length)
            {
                break;
            }

            var valueType = headers[pos++];
            if (valueType != 7)
            {
                // Skip over non-string header values by their fixed sizes
                int skipBytes = valueType switch
                {
                    0 => 1, // bool true
                    1 => 1, // bool false
                    2 => 2, // short
                    3 => 4, // int
                    4 => 8, // long
                    5 => 4, // float (not in AWS spec but defensive)
                    6 => 8, // double (not in AWS spec but defensive)
                    _ => -1 // unknown, must break
                };
                if (skipBytes < 0)
                {
                    break;
                }

                pos += skipBytes;
                continue;
            }

            if (pos + 2 > headers.Length)
            {
                break;
            }

            var valueLen = (headers[pos] << 8) | headers[pos + 1];
            pos += 2;
            if (pos + valueLen > headers.Length)
            {
                break;
            }

            var value = Encoding.UTF8.GetString(headers.Slice(pos, valueLen));
            pos += valueLen;

            if (key is ":event-type" or ":exception-type")
            {
                return value;
            }
        }

        return null;
    }

    private static uint ReadBigEndianUInt32(byte[] buffer, int offset) =>
        ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) |
        ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];

    private static async Task<bool> ReadExactAsync(
        Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0)
            {
                return false; // Stream ended
            }

            totalRead += read;
        }

        return true;
    }
}