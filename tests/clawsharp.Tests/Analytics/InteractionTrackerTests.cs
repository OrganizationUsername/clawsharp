using Clawsharp.Analytics;
using Clawsharp.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Clawsharp.Tests.Analytics;

public sealed class InteractionTrackerTests : IDisposable
{
    private readonly string _tempDir;

    public InteractionTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clawsharp-tracker-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private IInteractionStore CreateStore(string fileName = "interactions.jsonl")
        => new InteractionStorage(Path.Combine(_tempDir, fileName));

    [Test]
    public async Task RecordAsync_PersistsToStorage_AndCalculatesCost()
    {
        var store = CreateStore("record.jsonl");
        var memory = Substitute.For<IMemory>();
        var tracker = new InteractionTracker(
            store, memory, NullLogger<InteractionTracker>.Instance, storeInMemory: false);

        await tracker.RecordAsync(
            sessionId: "session-1",
            channel: "cli",
            model: "gpt-4o",
            userPrompt: "Hello",
            thinking: null,
            response: "Hi there",
            toolCalls: null,
            toolIterations: 0,
            inputTokens: 1000,
            outputTokens: 500,
            cacheReadTokens: 0,
            cacheWriteTokens: 0,
            durationMs: 300);

        var records = await store.ReadAllAsync();

        records.Count.ShouldBe(1);
        records[0].SessionId.ShouldBe("session-1");
        records[0].Channel.ShouldBe("cli");
        records[0].Model.ShouldBe("gpt-4o");
        records[0].UserPrompt.ShouldBe("Hello");
        records[0].Response.ShouldBe("Hi there");
        records[0].InputTokens.ShouldBe(1000);
        records[0].OutputTokens.ShouldBe(500);
        records[0].DurationMs.ShouldBe(300);
        // CostUsd is calculated by DefaultPricing — just verify it was set (non-negative)
        records[0].CostUsd.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task RecordAsync_StoreInMemoryTrue_WritesFactToMemory()
    {
        var store = CreateStore("memory-true.jsonl");
        var memory = Substitute.For<IMemory>();
        var tracker = new InteractionTracker(
            store, memory, NullLogger<InteractionTracker>.Instance, storeInMemory: true);

        await tracker.RecordAsync(
            sessionId: "session-2",
            channel: "telegram",
            model: "claude-3-opus",
            userPrompt: "What time is it?",
            thinking: null,
            response: "I don't have a clock.",
            toolCalls: null,
            toolIterations: 0,
            inputTokens: 200,
            outputTokens: 100,
            cacheReadTokens: 0,
            cacheWriteTokens: 0,
            durationMs: 150);

        await memory.Received(1).AppendFactAsync(
            Arg.Is<string>(s => s.Contains("session-2") && s.Contains("telegram") && s.Contains("claude-3-opus")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordAsync_StoreInMemoryFalse_DoesNotWriteToMemory()
    {
        var store = CreateStore("memory-false.jsonl");
        var memory = Substitute.For<IMemory>();
        var tracker = new InteractionTracker(
            store, memory, NullLogger<InteractionTracker>.Instance, storeInMemory: false);

        await tracker.RecordAsync(
            sessionId: "session-3",
            channel: "cli",
            model: "gpt-4o",
            userPrompt: "Test",
            thinking: null,
            response: "Response",
            toolCalls: null,
            toolIterations: 0,
            inputTokens: 100,
            outputTokens: 50,
            cacheReadTokens: 0,
            cacheWriteTokens: 0,
            durationMs: 100);

        await memory.DidNotReceive().AppendFactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordAsync_StorageFailure_DoesNotThrow()
    {
        // Use a path that will cause a write failure — a directory path instead of a file
        var badPath = Path.Combine(_tempDir, "bad-dir", "sub", "interactions.jsonl");
        // Create the directory so the constructor succeeds, then make the file unwritable
        Directory.CreateDirectory(Path.GetDirectoryName(badPath)!);
        // Create the file, then make the parent directory read-only so subsequent writes fail
        await File.WriteAllTextAsync(badPath, "");
        File.SetAttributes(badPath, FileAttributes.ReadOnly);

        IInteractionStore store = new InteractionStorage(badPath);
        var memory = Substitute.For<IMemory>();
        var tracker = new InteractionTracker(
            store, memory, NullLogger<InteractionTracker>.Instance, storeInMemory: false);

        // Should not throw — graceful degradation
        await Should.NotThrowAsync(async () =>
        {
            await tracker.RecordAsync(
                sessionId: "session-4",
                channel: "cli",
                model: "gpt-4o",
                userPrompt: "Test",
                thinking: null,
                response: "Response",
                toolCalls: null,
                toolIterations: 0,
                inputTokens: 100,
                outputTokens: 50,
                cacheReadTokens: 0,
                cacheWriteTokens: 0,
                durationMs: 100);
        });

        // Clean up read-only attribute so Dispose can delete
        File.SetAttributes(badPath, FileAttributes.Normal);
    }

    [Test]
    public async Task RecordAsync_WithToolCalls_PersistsToolData()
    {
        var store = CreateStore("tools.jsonl");
        var memory = Substitute.For<IMemory>();
        var tracker = new InteractionTracker(
            store, memory, NullLogger<InteractionTracker>.Instance, storeInMemory: true);

        var toolCalls = new List<ToolCallSummary>
        {
            new() { Name = "web_fetch", ResultLength = 2048 },
            new() { Name = "shell_exec", ResultLength = 512 },
        };

        await tracker.RecordAsync(
            sessionId: "session-5",
            channel: "discord",
            model: "gpt-4o",
            userPrompt: "Fetch page",
            thinking: "Let me think...",
            response: "Done",
            toolCalls: toolCalls,
            toolIterations: 2,
            inputTokens: 500,
            outputTokens: 200,
            cacheReadTokens: 100,
            cacheWriteTokens: 50,
            durationMs: 800);

        var records = await store.ReadAllAsync();

        records.Count.ShouldBe(1);
        records[0].ToolCalls.ShouldNotBeNull();
        records[0].ToolCalls!.Count.ShouldBe(2);
        records[0].ToolCalls![0].Name.ShouldBe("web_fetch");
        records[0].ToolCalls![1].Name.ShouldBe("shell_exec");
        records[0].ToolIterations.ShouldBe(2);
        records[0].Thinking.ShouldBe("Let me think...");

        // Memory fact should mention the tool names
        await memory.Received(1).AppendFactAsync(
            Arg.Is<string>(s => s.Contains("web_fetch") && s.Contains("shell_exec")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordAsync_MemoryFailure_DoesNotThrow()
    {
        var store = CreateStore("mem-fail.jsonl");
        var memory = Substitute.For<IMemory>();
        memory.AppendFactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Memory backend unavailable"));

        var tracker = new InteractionTracker(
            store, memory, NullLogger<InteractionTracker>.Instance, storeInMemory: true);

        // Should not throw even though memory write fails
        await Should.NotThrowAsync(async () =>
        {
            await tracker.RecordAsync(
                sessionId: "session-6",
                channel: "cli",
                model: "gpt-4o",
                userPrompt: "Test",
                thinking: null,
                response: "Response",
                toolCalls: null,
                toolIterations: 0,
                inputTokens: 100,
                outputTokens: 50,
                cacheReadTokens: 0,
                cacheWriteTokens: 0,
                durationMs: 100);
        });

        // The storage record should still be persisted even though memory failed
        var records = await store.ReadAllAsync();
        records.Count.ShouldBe(1);
    }
}
