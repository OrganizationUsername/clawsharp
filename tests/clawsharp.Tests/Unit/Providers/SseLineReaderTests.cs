using System.Text;
using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Providers;

[TestFixture]
public sealed class SseLineReaderTests
{
    private static Stream ToStream(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static async Task<List<(string? Event, string Data)>> ReadAllAsync(string text)
    {
        var results = new List<(string? Event, string Data)>();
        await foreach (var item in SseLineReader.ReadAsync(ToStream(text)))
        {
            results.Add(item);
        }
        return results;
    }

    // ── Single data line, blank-line dispatch ─────────────────────────

    [Test]
    public async Task ReadAsync_SingleDataLine_YieldsOneEvent()
    {
        var events = await ReadAllAsync("data: hello\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("hello");
        events[0].Event.ShouldBeNull();
    }

    [Test]
    public async Task ReadAsync_EmptyStream_YieldsNoEvents()
    {
        var events = await ReadAllAsync("");

        events.ShouldBeEmpty();
    }

    // ── Multi-line data accumulation ──────────────────────────────────

    [Test]
    public async Task ReadAsync_MultipleDataLines_JoinedWithNewline()
    {
        var events = await ReadAllAsync("data: line1\ndata: line2\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("line1\nline2");
    }

    [Test]
    public async Task ReadAsync_ThreeDataLines_AllJoined()
    {
        var events = await ReadAllAsync("data: a\ndata: b\ndata: c\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("a\nb\nc");
    }

    // ── Event field ───────────────────────────────────────────────────

    [Test]
    public async Task ReadAsync_EventField_PopulatesEventType()
    {
        var events = await ReadAllAsync("event: message_start\ndata: payload\n\n");

        events.Count.ShouldBe(1);
        events[0].Event.ShouldBe("message_start");
        events[0].Data.ShouldBe("payload");
    }

    [Test]
    public async Task ReadAsync_EventField_ResetToNullAfterDispatch()
    {
        // First event has an event field; second does not — event must reset to null
        var events = await ReadAllAsync(
            "event: message_start\ndata: first\n\n" +
            "data: second\n\n");

        events.Count.ShouldBe(2);
        events[0].Event.ShouldBe("message_start");
        events[1].Event.ShouldBeNull("event field must reset after blank-line dispatch");
    }

    // ── Comment lines ─────────────────────────────────────────────────

    [Test]
    public async Task ReadAsync_CommentLine_Ignored()
    {
        var events = await ReadAllAsync(": keep-alive ping\ndata: actual\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("actual");
    }

    [Test]
    public async Task ReadAsync_CommentBetweenDataLines_DoesNotBreakAccumulation()
    {
        var events = await ReadAllAsync("data: first\n: heartbeat\ndata: second\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("first\nsecond");
    }

    // ── End-of-stream flush ───────────────────────────────────────────

    [Test]
    public async Task ReadAsync_NoTrailingBlankLine_PendingDataFlushedAtEndOfStream()
    {
        // Stream ends without a blank-line delimiter — pending data must still be yielded
        var events = await ReadAllAsync("data: final_chunk");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("final_chunk");
    }

    [Test]
    public async Task ReadAsync_NoTrailingBlankLine_WithEventField_FlushedCorrectly()
    {
        var events = await ReadAllAsync("event: done\ndata: payload");

        events.Count.ShouldBe(1);
        events[0].Event.ShouldBe("done");
        events[0].Data.ShouldBe("payload");
    }

    // ── Multiple events ───────────────────────────────────────────────

    [Test]
    public async Task ReadAsync_TwoEvents_BothYielded()
    {
        var events = await ReadAllAsync(
            "data: first\n\n" +
            "data: second\n\n");

        events.Count.ShouldBe(2);
        events[0].Data.ShouldBe("first");
        events[1].Data.ShouldBe("second");
    }

    [Test]
    public async Task ReadAsync_OpenAiDoneMarker_PassedThroughAsData()
    {
        // [DONE] sentinel is defined by the caller's protocol; SseLineReader passes it through
        var events = await ReadAllAsync("data: {\"delta\":\"hi\"}\n\ndata: [DONE]\n\n");

        events.Count.ShouldBe(2);
        events[1].Data.ShouldBe("[DONE]");
    }

    // ── Space after colon is optional ─────────────────────────────────

    [Test]
    public async Task ReadAsync_DataWithoutSpaceAfterColon_Parsed()
    {
        // "data:value" (no space) is valid SSE
        var events = await ReadAllAsync("data:no-space\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("no-space");
    }

    [Test]
    public async Task ReadAsync_DataWithSpaceAfterColon_SpaceStripped()
    {
        // "data: value" — one leading space is stripped per SSE spec
        var events = await ReadAllAsync("data: spaced\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("spaced");
    }

    // ── Bare field names (no colon) ───────────────────────────────────

    [Test]
    public async Task ReadAsync_BareFieldName_IgnoredNoEventYielded()
    {
        // A line with no colon is silently skipped by this implementation
        var events = await ReadAllAsync("data\n\n");

        // No data was accumulated — blank line after bare field should yield nothing
        events.ShouldBeEmpty();
    }

    // ── Blank-line reset clears event name ────────────────────────────

    [Test]
    public async Task ReadAsync_BlankLineWithoutData_DoesNotYieldEvent()
    {
        // A blank line with no accumulated data must not yield an empty event
        var events = await ReadAllAsync("\n\ndata: real\n\n");

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBe("real");
    }

    // ── Realistic LLM provider stream patterns ────────────────────────

    [Test]
    public async Task ReadAsync_AnthropicStyleStream_ParsesCorrectly()
    {
        // Anthropic SSE: event: + data: pairs
        const string stream =
            "event: content_block_delta\ndata: {\"delta\":{\"text\":\"Hello\"}}\n\n" +
            "event: content_block_delta\ndata: {\"delta\":{\"text\":\" world\"}}\n\n" +
            "event: message_stop\ndata: {}\n\n";

        var events = await ReadAllAsync(stream);

        events.Count.ShouldBe(3);
        events[0].Event.ShouldBe("content_block_delta");
        events[0].Data.ShouldBe("{\"delta\":{\"text\":\"Hello\"}}");
        events[2].Event.ShouldBe("message_stop");
    }

    [Test]
    public async Task ReadAsync_OpenAiStyleStream_ParsesCorrectly()
    {
        // OpenAI SSE: just data: lines, no event field
        const string stream =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"!\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var events = await ReadAllAsync(stream);

        events.Count.ShouldBe(3);
        events.ShouldAllBe(e => e.Event == null);
        events[2].Data.ShouldBe("[DONE]");
    }
}
