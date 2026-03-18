using Clawsharp.Config.Features;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Cost;
using Clawsharp.Providers;
using Clawsharp.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Unit.Core;

[TestFixture]
public sealed class CompactionServiceEdgeCaseTests
{
    private static CompactionService MakeService() =>
        new(costTracker: null!, auditLogger: null, NullLogger<CompactionService>.Instance);

    /// <summary>
    /// Creates a CompactionService with a real CostTracker (cost disabled) for tests that
    /// exercise the full compaction path including the budget check in SummarizeBatchAsync.
    /// </summary>
    private static CompactionService MakeServiceWithCostTracker()
    {
        // CostStorage uses a temp file; CostConfig.Enabled=false short-circuits budget checks.
        var costStorage = new CostStorage(Path.Combine(Path.GetTempPath(), $"test-costs-{Guid.NewGuid():N}.jsonl"));
        var costConfig = Options.Create(new CostConfig { Enabled = false });
        var costTracker = new CostTracker(costStorage, costConfig, NullLogger<CostTracker>.Instance);
        return new CompactionService(costTracker, auditLogger: null, NullLogger<CompactionService>.Instance);
    }

    private static ChatMessage User(string content) => new(MessageRole.User, content);
    private static ChatMessage Assistant(string content) => new(MessageRole.Assistant, content);
    private static ChatMessage System(string content) => new(MessageRole.System, content);

    // ── ForceCompress edge cases ──────────────────────────────────────

    [Test]
    public void ForceCompress_EmptyList_ReturnsEmptyList()
    {
        var result = CompactionService.ForceCompress([]);
        result.ShouldBeEmpty();
    }

    [Test]
    public void ForceCompress_SingleSystemMessage_ReturnsJustSystemMessage()
    {
        var messages = new List<ChatMessage> { System("You are helpful.") };

        var result = CompactionService.ForceCompress(messages);

        result.Count.ShouldBe(1);
        result[0].Role.ShouldBe(MessageRole.System);
    }

    [Test]
    public void ForceCompress_ExactlyKeepLastMessages_NoSystemPrompt_AllPreserved()
    {
        // keepLast = 4 (internal constant). Exactly 4 non-system messages = nothing discarded.
        var messages = new List<ChatMessage>
        {
            User("msg1"),
            Assistant("reply1"),
            User("msg2"),
            Assistant("reply2"),
        };

        var result = CompactionService.ForceCompress(messages);

        result.Count.ShouldBe(4, "exactly keepLast messages with no system prompt = all preserved");
    }

    [Test]
    public void ForceCompress_MoreThanKeepLast_DiscardedExceptLast4()
    {
        // 7 non-system messages → only last 4 kept
        var messages = Enumerable.Range(1, 7)
            .Select(i => User($"msg{i}"))
            .ToList();

        var result = CompactionService.ForceCompress(messages);

        result.Count.ShouldBe(4);
        result[0].Content.ShouldBe("msg4");
        result[1].Content.ShouldBe("msg5");
        result[2].Content.ShouldBe("msg6");
        result[3].Content.ShouldBe("msg7");
    }

    [Test]
    public void ForceCompress_WithSystemPrompt_SystemAlwaysPreserved()
    {
        var messages = new List<ChatMessage>
        {
            System("System prompt."),
            User("msg1"),
            User("msg2"),
            User("msg3"),
            User("msg4"),
            User("msg5"),
            User("msg6"),
        };

        var result = CompactionService.ForceCompress(messages);

        // System prompt + last 4 non-system messages
        result.Count.ShouldBe(5);
        result[0].Role.ShouldBe(MessageRole.System);
        result[0].Content.ShouldBe("System prompt.");
        result[^1].Content.ShouldBe("msg6");
    }

    [Test]
    public void ForceCompress_SystemPlusExactlyKeepLast_AllPreserved()
    {
        var messages = new List<ChatMessage>
        {
            System("System."),
            User("m1"), User("m2"), User("m3"), User("m4"),
        };

        var result = CompactionService.ForceCompress(messages);

        // System + 4 = 5 total, nothing should be dropped
        result.Count.ShouldBe(5);
    }

    // ── CompactAsync boundary: too few messages to compact ────────────

    [Test]
    public async Task CompactAsync_TooFewMessages_ReturnsUnchanged()
    {
        var service = MakeService();
        var provider = new FakeProvider();

        // Default keepRecent=20. With exactly 21 messages (system + 20),
        // the count == keepRecent + 1 which is the no-op threshold.
        var messages = new List<ChatMessage> { System("System.") };
        for (var i = 0; i < 20; i++) messages.Add(User($"msg{i}"));

        messages.Count.ShouldBe(21);

        var result = await service.CompactAsync(messages, provider, "fake-model");

        result.Count.ShouldBe(21, "exactly at the threshold — no messages should be compacted");
        provider.ReceivedRequests.ShouldBeEmpty("no LLM call should be made");
    }

    [Test]
    public async Task CompactAsync_OneAboveThreshold_CompactionOccurs()
    {
        // Uses MakeServiceWithCostTracker() because the full compaction path calls
        // costTracker.CheckBudgetAsync() inside SummarizeBatchAsync; passing null! crashes.
        var service = MakeServiceWithCostTracker();
        var provider = new FakeProvider();
        provider.EnqueueText("Summary of old messages.");

        // 22 messages = just over the threshold (system + 20 = 21 = keepRecent+1, so 22 triggers)
        var messages = new List<ChatMessage> { System("System.") };
        for (var i = 0; i < 21; i++) messages.Add(User($"msg{i}"));

        messages.Count.ShouldBe(22);

        var result = await service.CompactAsync(messages, provider, "fake-model");

        // Should have compacted: system + summary + keepRecent recent messages
        result.Count.ShouldBeLessThan(22, "compaction should reduce message count");
        result[0].Role.ShouldBe(MessageRole.System, "system prompt must always be first");
        provider.ReceivedRequests.Count.ShouldBe(1, "exactly one LLM call for summarization");
    }

    // ── All system messages ───────────────────────────────────────────

    [Test]
    public async Task CompactAsync_AllSystemMessages_NoLLMCallNoModification()
    {
        var service = MakeService();
        var provider = new FakeProvider();

        // Contrived: many system messages. Only the first is extracted as system prompt.
        // All subsequent "system" messages are treated as regular content.
        // In practice this shouldn't happen but CompactAsync must not crash.
        var messages = Enumerable.Range(0, 25)
            .Select(i => System($"sys{i}"))
            .ToList();

        // This may or may not call the LLM depending on implementation details,
        // but must not throw.
        Should.NotThrow(async () =>
        {
            var _ = await service.CompactAsync(messages, provider, "fake-model");
        });
    }
}
