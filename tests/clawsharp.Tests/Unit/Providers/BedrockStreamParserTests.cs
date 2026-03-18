using System.Text;
using Clawsharp.Providers.Bedrock;

namespace Clawsharp.Tests.Unit.Providers;

[TestFixture]
public sealed class BedrockStreamParserTests
{
    // ── Binary event-stream message builder ──────────────────────────

    /// <summary>
    /// Builds a minimal AWS binary event-stream message.
    /// Format: [total_length(4)] [headers_length(4)] [prelude_crc(4)]
    ///         [headers] [payload] [message_crc(4)]
    /// CRCs are set to zero for testing purposes (the parser skips them per TLS trust).
    /// </summary>
    private static byte[] BuildMessage(string eventType, string payloadJson,
        bool useExceptionType = false)
    {
        var headerKey = useExceptionType ? ":exception-type" : ":event-type";
        var headerKeyBytes = Encoding.UTF8.GetBytes(headerKey);
        var headerValueBytes = Encoding.UTF8.GetBytes(eventType);

        // Header format: [1-byte key len][key][1-byte value type (7=string)][2-byte value len][value]
        var headerBytes = new byte[
            1 + headerKeyBytes.Length +   // key length + key
            1 +                            // value type = 7 (string)
            2 + headerValueBytes.Length    // value length + value
        ];

        var pos = 0;
        headerBytes[pos++] = (byte)headerKeyBytes.Length;
        headerKeyBytes.CopyTo(headerBytes, pos);
        pos += headerKeyBytes.Length;
        headerBytes[pos++] = 7; // string type
        headerBytes[pos++] = (byte)(headerValueBytes.Length >> 8);
        headerBytes[pos++] = (byte)(headerValueBytes.Length & 0xFF);
        headerValueBytes.CopyTo(headerBytes, pos);

        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // total = prelude(12) + headers + payload + message_crc(4)
        var totalLength = 12 + headerBytes.Length + payloadBytes.Length + 4;

        using var ms = new MemoryStream();
        WriteUInt32BE(ms, (uint)totalLength);
        WriteUInt32BE(ms, (uint)headerBytes.Length);
        WriteUInt32BE(ms, 0); // prelude CRC (skipped by parser)
        ms.Write(headerBytes);
        ms.Write(payloadBytes);
        WriteUInt32BE(ms, 0); // message CRC (skipped by parser)
        return ms.ToArray();
    }

    private static void WriteUInt32BE(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static async Task<List<(string EventType, string Payload)>> ParseAll(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var results = new List<(string, string)>();
        await foreach (var (evt, payload) in BedrockStreamParser.ParseAsync(ms))
        {
            results.Add((evt, Encoding.UTF8.GetString(payload.Span)));
        }
        return results;
    }

    // ── Basic parsing ─────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_SingleEventTypeMessage_YieldsCorrectEvent()
    {
        var data = BuildMessage("contentBlockDelta", """{"delta":{"text":"hello"}}""");
        var results = await ParseAll(data);

        results.Count.ShouldBe(1);
        results[0].EventType.ShouldBe("contentBlockDelta");
        results[0].Payload.ShouldContain("hello");
    }

    [Test]
    public async Task ParseAsync_MultipleMessages_AllYielded()
    {
        using var ms = new MemoryStream();
        var m1 = BuildMessage("contentBlockStart", "{}");
        var m2 = BuildMessage("contentBlockDelta", """{"delta":{"text":"Hi"}}""");
        var m3 = BuildMessage("contentBlockStop", "{}");
        ms.Write(m1);
        ms.Write(m2);
        ms.Write(m3);
        ms.Seek(0, SeekOrigin.Begin);

        var results = new List<(string EventType, string Payload)>();
        await foreach (var item in BedrockStreamParser.ParseAsync(ms))
        {
            results.Add((item.EventType, Encoding.UTF8.GetString(item.Payload.Span)));
        }

        results.Count.ShouldBe(3);
        results[0].EventType.ShouldBe("contentBlockStart");
        results[1].EventType.ShouldBe("contentBlockDelta");
        results[2].EventType.ShouldBe("contentBlockStop");
    }

    // ── Exception type header ─────────────────────────────────────────

    [Test]
    public async Task ParseAsync_ExceptionTypeHeader_YieldedCorrectly()
    {
        // Bedrock signals throttling and other errors via :exception-type headers
        var data = BuildMessage("throttlingException", """{"message":"Rate exceeded"}""",
            useExceptionType: true);

        var results = await ParseAll(data);

        results.Count.ShouldBe(1);
        results[0].EventType.ShouldBe("throttlingException");
        results[0].Payload.ShouldContain("Rate exceeded");
    }

    // ── Empty stream ──────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_EmptyStream_YieldsNoEvents()
    {
        using var ms = new MemoryStream();
        var results = new List<(string, ReadOnlyMemory<byte>)>();
        await foreach (var item in BedrockStreamParser.ParseAsync(ms))
        {
            results.Add(item);
        }

        results.ShouldBeEmpty();
    }

    // ── Minimum total length (12 bytes = just prelude, no payload) ────

    [Test]
    public async Task ParseAsync_TotalLengthEqualsTwelve_SkippedWithNoException()
    {
        // totalLength=12 means remaining=0 → the parser should skip this message
        using var ms = new MemoryStream();
        WriteUInt32BE(ms, 12); // totalLength
        WriteUInt32BE(ms, 0);  // headersLength
        WriteUInt32BE(ms, 0);  // preludeCRC
        ms.Seek(0, SeekOrigin.Begin);

        var results = new List<(string, ReadOnlyMemory<byte>)>();
        Should.NotThrow(async () =>
        {
            await foreach (var item in BedrockStreamParser.ParseAsync(ms))
            {
                results.Add(item);
            }
        });

        results.ShouldBeEmpty("a message with no payload should be silently skipped");
    }

    // ── Truncated stream ──────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_TruncatedAfterPrelude_YieldsNoEventsNoException()
    {
        // Write only the 12-byte prelude declaring a 100-byte message,
        // but provide no remaining bytes — stream ends early.
        using var ms = new MemoryStream();
        WriteUInt32BE(ms, 100); // claims 100 bytes total
        WriteUInt32BE(ms, 0);
        WriteUInt32BE(ms, 0);
        // No remaining bytes
        ms.Seek(0, SeekOrigin.Begin);

        Should.NotThrow(async () =>
        {
            await foreach (var _ in BedrockStreamParser.ParseAsync(ms)) { }
        });
    }
}
