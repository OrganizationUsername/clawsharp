using System.Text;
using Clawsharp.Core;
using Clawsharp.Tests.Fakes;

namespace Clawsharp.Tests;

[TestFixture]
public sealed class ProviderStreamingTests
{
    private static ChatRequest MakeRequest(string prompt = "Hello")
    {
        return new ChatRequest(
            "test-model",
            [new ChatMessage(MessageRole.User, prompt)]);
    }

    // ── 1. Text-only stream ──

    [Test]
    public async Task StreamAsync_TextOnlyStream_YieldsTextDeltasAndDone()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueTextStream("Hello", " ", "World");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            chunks.Add(chunk);
        }

        chunks.Count.ShouldBe(4); // 3 TextDelta + 1 StreamDone
        chunks[0].ShouldBeOfType<TextDeltaChunk>().Delta.ShouldBe("Hello");
        chunks[1].ShouldBeOfType<TextDeltaChunk>().Delta.ShouldBe(" ");
        chunks[2].ShouldBeOfType<TextDeltaChunk>().Delta.ShouldBe("World");
        chunks[3].ShouldBeOfType<StreamDoneChunk>();
    }

    // ── 2. Tool call argument reconstruction ──

    [Test]
    public async Task StreamAsync_ToolCallStream_ReconstructsArgumentsJson()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueToolCallStream("call_1", "file_read", """{"pa""", """th":""", """ "/tmp/x"}""");

        var argsBuilder = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            if (chunk is ToolCallChunk { Index: 0, ArgumentsDelta: { } delta })
            {
                argsBuilder.Append(delta);
            }
        }

        argsBuilder.ToString().ShouldBe("""{"path": "/tmp/x"}""");
    }

    // ── 3. Multiple tool calls at different indices ──

    [Test]
    public async Task StreamAsync_MultipleToolCallsAtDifferentIndices_TracksAllTools()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueChunks(
            new ToolCallChunk(0, "call_a", "tool_a", null),
            new ToolCallChunk(0, null, null, """{"x":1}"""),
            new ToolCallChunk(1, "call_b", "tool_b", null),
            new ToolCallChunk(1, null, null, """{"y":2}"""),
            new StreamDoneChunk());

        var tools = new Dictionary<int, (string? Id, string? Name, StringBuilder Args)>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            if (chunk is ToolCallChunk tc)
            {
                if (!tools.TryGetValue(tc.Index, out var entry))
                {
                    entry = (tc.Id, tc.Name, new StringBuilder());
                    tools[tc.Index] = entry;
                }

                if (tc.ArgumentsDelta is not null)
                {
                    tools[tc.Index].Args.Append(tc.ArgumentsDelta);
                }
            }
        }

        tools.Count.ShouldBe(2);
        tools[0].Id.ShouldBe("call_a");
        tools[0].Name.ShouldBe("tool_a");
        tools[0].Args.ToString().ShouldBe("""{"x":1}""");
        tools[1].Id.ShouldBe("call_b");
        tools[1].Name.ShouldBe("tool_b");
        tools[1].Args.ToString().ShouldBe("""{"y":2}""");
    }

    // ── 4. Thinking then text ordering ──

    [Test]
    public async Task StreamAsync_ThinkingThenText_YieldsChunksInOrder()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueThinkingThenTextStream("Let me reason...", "Answer:", " 42");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            chunks.Add(chunk);
        }

        // ThinkingDelta, TextDelta, TextDelta, StreamDone
        chunks.Count.ShouldBe(4);
        chunks[0].ShouldBeOfType<ThinkingDeltaChunk>().Delta.ShouldBe("Let me reason...");
        chunks[1].ShouldBeOfType<TextDeltaChunk>().Delta.ShouldBe("Answer:");
        chunks[2].ShouldBeOfType<TextDeltaChunk>().Delta.ShouldBe(" 42");
        chunks[3].ShouldBeOfType<StreamDoneChunk>();
    }

    // ── 5. Empty queue throws ──

    [Test]
    public void StreamAsync_EmptyQueue_ThrowsInvalidOperation()
    {
        var provider = new FakeStreamingProvider();

        Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(MakeRequest()))
            {
            }
        });
    }

    // ── 6. Request is recorded ──

    [Test]
    public async Task StreamAsync_AnyRequest_RecordsReceivedRequest()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueTextStream("ok");

        await foreach (var _ in provider.StreamAsync(MakeRequest("test prompt")))
        {
        }

        provider.ReceivedRequests.Count.ShouldBe(1);
        provider.ReceivedRequests[0].Messages[0].Content.ShouldBe("test prompt");
    }

    // ── 7. Cancellation stops iteration ──

    [Test]
    public async Task StreamAsync_CancellationAfterTwoChunks_StopsIteration()
    {
        var provider = new FakeStreamingProvider();
        // Enqueue a stream with many chunks
        provider.EnqueueChunks(
            new TextDeltaChunk("a"),
            new TextDeltaChunk("b"),
            new TextDeltaChunk("c"),
            new TextDeltaChunk("d"),
            new TextDeltaChunk("e"),
            new StreamDoneChunk());

        using var cts = new CancellationTokenSource();
        var received = new List<StreamChunk>();

        // Cancel after first chunk
        await foreach (var chunk in provider.StreamAsync(MakeRequest(), cts.Token))
        {
            received.Add(chunk);
            if (received.Count == 2)
            {
                await cts.CancelAsync();
                break;
            }
        }

        received.Count.ShouldBe(2);
    }

    // ── 8. Non-streaming ChatAsync (FakeProvider) ──

    [Test]
    public async Task ChatAsync_TextResponse_ReturnsChatResponse()
    {
        var provider = new FakeProvider();
        provider.EnqueueText("hi");

        var response = await provider.ChatAsync(MakeRequest());

        response.Content.ShouldBe("hi");
        response.FinishReason.ShouldBe(FinishReason.Stop);
        response.ToolCalls.ShouldBeNull();
    }

    // ── 9. ChatAsync with tool call ──

    [Test]
    public async Task ChatAsync_ToolCallResponse_ReturnsToolCallsInResponse()
    {
        var provider = new FakeProvider();
        provider.EnqueueToolCall("tc_1", "shell", """{"command":"ls"}""");

        var response = await provider.ChatAsync(MakeRequest());

        response.Content.ShouldBeNull();
        response.FinishReason.ShouldBe(FinishReason.ToolCalls);
        response.ToolCalls.ShouldNotBeNull();
        response.ToolCalls!.Count.ShouldBe(1);
        response.ToolCalls[0].Id.ShouldBe("tc_1");
        response.ToolCalls[0].Name.ShouldBe("shell");
        response.ToolCalls[0].ArgumentsJson.ShouldBe("""{"command":"ls"}""");
    }

    // ── 10. StreamDone has finish reason (it's a sentinel — no FinishReason field) ──

    [Test]
    public async Task StreamAsync_TextStream_StreamDoneIsLastChunk()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueTextStream("done");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            chunks.Add(chunk);
        }

        // StreamDoneChunk is the terminal sentinel
        var last = chunks[^1];
        last.ShouldBeOfType<StreamDoneChunk>();
        // All preceding chunks are text deltas
        chunks.SkipLast(1).ShouldAllBe(c => c is TextDeltaChunk);
    }

    // ── 11. Text deltas can be concatenated ──

    [Test]
    public async Task StreamAsync_TextDeltas_ConcatenateToFullText()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueTextStream("Hello", " ", "World");

        var sb = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            if (chunk is TextDeltaChunk text)
            {
                sb.Append(text.Delta);
            }
        }

        sb.ToString().ShouldBe("Hello World");
    }

    // ── 12. Multiple enqueued streams consumed sequentially ──

    [Test]
    public async Task StreamAsync_MultipleEnqueuedStreams_ConsumedSequentially()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueTextStream("first");
        provider.EnqueueTextStream("second");

        // First call
        var first = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            first.Add(chunk);
        }

        // Second call
        var second = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            second.Add(chunk);
        }

        first.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("first");
        second.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("second");

        // Both calls recorded
        provider.ReceivedRequests.Count.ShouldBe(2);
    }

    // ── 13. ChatAsync and StreamAsync use separate queues ──

    [Test]
    public async Task ChatAsync_SeparateFromStreamQueue_UsesOwnQueue()
    {
        var provider = new FakeStreamingProvider();
        provider.EnqueueChatResponse(new ChatResponse("chat-reply", null, FinishReason.Stop));
        provider.EnqueueTextStream("stream-reply");

        // ChatAsync uses ChatQueue
        var chatResponse = await provider.ChatAsync(MakeRequest());
        chatResponse.Content.ShouldBe("chat-reply");

        // StreamAsync uses StreamQueue (unaffected by ChatAsync call)
        var streamChunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MakeRequest()))
        {
            streamChunks.Add(chunk);
        }

        streamChunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("stream-reply");

        // Both requests recorded
        provider.ReceivedRequests.Count.ShouldBe(2);
    }
}