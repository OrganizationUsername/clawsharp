using System.Runtime.CompilerServices;
using System.Text.Json;
using Clawsharp.Channels;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Memory;
using Clawsharp.Memory.Entities;
using Clawsharp.Providers;
using Clawsharp.Tools;

namespace Clawsharp.Tests.Fakes;

// ──────────────────────────────────────────────────────────────────────
//  FakeProvider — non-streaming IProvider with scripted responses
// ──────────────────────────────────────────────────────────────────────

public sealed class FakeProvider : IProvider
{
    public string Name => "fake";

    public bool SupportsVision => false;

    public Queue<ChatResponse> Responses { get; } = new();

    public List<ChatRequest> ReceivedRequests { get; } = [];

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);

        if (Responses.Count == 0)
        {
            throw new InvalidOperationException("FakeProvider: no responses queued.");
        }

        return Task.FromResult(Responses.Dequeue());
    }

    // ── Helpers ──

    public void EnqueueText(string text)
    {
        Responses.Enqueue(new ChatResponse(text, null, FinishReason.Stop));
    }

    public void EnqueueToolCall(string id, string name, string argsJson)
    {
        Responses.Enqueue(new ChatResponse(
            null,
            [new ToolCall(id, name, argsJson)],
            FinishReason.ToolCalls));
    }

    public void EnqueueWithReasoning(string text, string reasoning)
    {
        Responses.Enqueue(new ChatResponse(text, null, FinishReason.Stop, ReasoningContent: reasoning));
    }
}

// ──────────────────────────────────────────────────────────────────────
//  FakeStreamingProvider — IStreamingProvider with scripted chunk sequences
// ──────────────────────────────────────────────────────────────────────

public sealed class FakeStreamingProvider : IStreamingProvider
{
    public string Name => "fake-streaming";

    public bool SupportsVision => false;

    /// <summary>Queue of chunk lists for StreamAsync calls.</summary>
    public Queue<IReadOnlyList<StreamChunk>> StreamQueue { get; } = new();

    /// <summary>Separate queue for non-streaming ChatAsync calls.</summary>
    public Queue<ChatResponse> ChatQueue { get; } = new();

    public List<ChatRequest> ReceivedRequests { get; } = [];

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);

        if (ChatQueue.Count == 0)
        {
            throw new InvalidOperationException("FakeStreamingProvider: no ChatAsync responses queued.");
        }

        return Task.FromResult(ChatQueue.Dequeue());
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);

        if (StreamQueue.Count == 0)
        {
            throw new InvalidOperationException("FakeStreamingProvider: no stream chunks queued.");
        }

        var chunks = StreamQueue.Dequeue();
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }

    // ── Helpers ──

    public void EnqueueTextStream(params string[] deltas)
    {
        var chunks = new List<StreamChunk>();
        foreach (var d in deltas)
        {
            chunks.Add(new TextDeltaChunk(d));
        }

        chunks.Add(new StreamDoneChunk());
        StreamQueue.Enqueue(chunks);
    }

    public void EnqueueToolCallStream(string id, string name, params string[] argFragments)
    {
        var chunks = new List<StreamChunk>
        {
            new ToolCallChunk(0, id, name, null)
        };
        foreach (var frag in argFragments)
        {
            chunks.Add(new ToolCallChunk(0, null, null, frag));
        }

        chunks.Add(new StreamDoneChunk());
        StreamQueue.Enqueue(chunks);
    }

    public void EnqueueThinkingThenTextStream(string thinking, params string[] textDeltas)
    {
        var chunks = new List<StreamChunk> { new ThinkingDeltaChunk(thinking) };
        foreach (var d in textDeltas)
        {
            chunks.Add(new TextDeltaChunk(d));
        }

        chunks.Add(new StreamDoneChunk());
        StreamQueue.Enqueue(chunks);
    }

    public void EnqueueChunks(params StreamChunk[] chunks)
    {
        StreamQueue.Enqueue(chunks);
    }

    public void EnqueueChatResponse(ChatResponse response)
    {
        ChatQueue.Enqueue(response);
    }
}

// ──────────────────────────────────────────────────────────────────────
//  FakeChannel — minimal IChannel capturing sent messages
// ──────────────────────────────────────────────────────────────────────

public class FakeChannel : IChannel
{
    public ChannelName Name => ChannelName.Cli;

    public List<string> SentMessages { get; } = [];

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        SentMessages.Add(message.Text);
        return Task.CompletedTask;
    }
}

// ──────────────────────────────────────────────────────────────────────
//  FakeStreamingChannel — IStreamingChannel capturing streamed tokens
// ──────────────────────────────────────────────────────────────────────

public sealed class FakeStreamingChannel : FakeChannel, IStreamingChannel
{
    /// <summary>One List&lt;string&gt; per StreamAsync call, containing all tokens from that call.</summary>
    public List<List<string>> StreamedTokenBatches { get; } = [];

    public async Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        var batch = new List<string>();
        await foreach (var token in tokens.WithCancellation(ct))
        {
            batch.Add(token);
        }

        StreamedTokenBatches.Add(batch);
    }
}

// ──────────────────────────────────────────────────────────────────────
//  FakeMemory — in-memory IMemory implementation
// ──────────────────────────────────────────────────────────────────────

public sealed class FakeMemory : IMemory
{
    public List<string> Facts { get; } = [];

    public List<string> History { get; } = [];

    public Task<string?> GetContextAsync(CancellationToken ct = default)
    {
        if (Facts.Count == 0 && History.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var parts = new List<string>();
        if (Facts.Count > 0)
        {
            parts.Add("Facts: " + string.Join("; ", Facts));
        }

        if (History.Count > 0)
        {
            parts.Add("History: " + string.Join("; ", History));
        }

        return Task.FromResult<string?>(string.Join("\n", parts));
    }

    public Task AppendFactAsync(string fact, CancellationToken ct = default)
    {
        Facts.Add(fact);
        return Task.CompletedTask;
    }

    public Task AppendHistoryAsync(string summary, CancellationToken ct = default)
    {
        History.Add(summary);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SearchAsync(string query, int n = 5, CancellationToken ct = default)
    {
        IReadOnlyList<string> result = Facts
                                       .Where(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))
                                       .Take(n)
                                       .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Fact>> ListFactsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Fact> result = Facts
                                     .Select((f, i) => new Fact { Id = i + 1, Content = f, CreatedAt = DateTimeOffset.UtcNow })
                                     .ToList();
        return Task.FromResult(result);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        Facts.Clear();
        History.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Fact>> SearchHybridAsync(string query, float[]? queryEmbedding = null, int topK = 5,
                                                       CancellationToken ct = default)
    {
        IReadOnlyList<Fact> result = Facts
                                     .Where(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))
                                     .Take(topK)
                                     .Select((f, i) => new Fact { Id = i + 1, Content = f, CreatedAt = DateTimeOffset.UtcNow })
                                     .ToList();
        return Task.FromResult(result);
    }

    public Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        return Task.FromResult(0);
    }
}

// ──────────────────────────────────────────────────────────────────────
//  FakeToolRegistry — IToolRegistry with per-tool result queues
// ──────────────────────────────────────────────────────────────────────

public sealed class FakeToolRegistry : IToolRegistry
{
    private readonly List<ToolDefinition> _definitions = [];

    private readonly Dictionary<string, Queue<string>> _queues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Recorded (Name, ArgsJson) for each ExecuteAsync call.</summary>
    public List<(string Name, string ArgsJson)> ExecutedCalls { get; } = [];

    public void Register(Tool tool)
    {
        _definitions.Add(tool.ToDefinition());
    }

    public void SetChannelContext(string channelName, int spawnDepth = 0, string? sessionId = null)
    {
    }

    public IReadOnlyList<ToolDefinition> GetDefinitions() => _definitions;

    public IReadOnlyList<ToolDefinition> GetFilteredDefinitions(string? messageText) => _definitions;

    public Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken ct = default)
    {
        ExecutedCalls.Add((name, argumentsJson));

        if (_queues.TryGetValue(name, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(queue.Dequeue());
        }

        return Task.FromResult($"[fake result for {name}]");
    }

    // ── Helpers ──

    public void AddDefinition(string name, string? description = null, string? schema = null)
    {
        _definitions.Add(new ToolDefinition(
            name,
            description ?? $"{name} tool",
            schema ?? """{"type":"object","properties":{}}"""));
    }

    public void EnqueueResult(string toolName, string result)
    {
        if (!_queues.TryGetValue(toolName, out var queue))
        {
            queue = new Queue<string>();
            _queues[toolName] = queue;
        }

        queue.Enqueue(result);
    }
}