using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Core;

[TestFixture]
public sealed class MergeConsecutiveRolesTests
{
    // ── Trivial cases ─────────────────────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_EmptyList_ReturnsEmptyList()
    {
        var result = AgentLoop.MergeConsecutiveRoles([]);
        result.ShouldBeEmpty();
    }

    [Test]
    public void MergeConsecutiveRoles_SingleMessage_ReturnsSingleMessage()
    {
        var msg = new ChatMessage(MessageRole.User, "hello");
        var result = AgentLoop.MergeConsecutiveRoles([msg]);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe("hello");
    }

    [Test]
    public void MergeConsecutiveRoles_SingleMessageWithNullContent_ReturnsSingleMessage()
    {
        var msg = new ChatMessage(MessageRole.User, null);
        var result = AgentLoop.MergeConsecutiveRoles([msg]);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBeNull();
    }

    // ── User-user merging ─────────────────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_TwoConsecutiveUserMessages_Merged()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, "first"),
            new(MessageRole.User, "second"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(1);
        result[0].Role.ShouldBe(MessageRole.User);
        result[0].Content.ShouldBe("first\n\nsecond");
    }

    [Test]
    public void MergeConsecutiveRoles_ThreeConsecutiveUserMessages_AllMerged()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, "a"),
            new(MessageRole.User, "b"),
            new(MessageRole.User, "c"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe("a\n\nb\n\nc");
    }

    // ── Null content merging ──────────────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_BothMessagesHaveNullContent_MergedToEmpty()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, null),
            new(MessageRole.User, null),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(1);
        // ("" + "\n\n" + "").Trim() == ""
        result[0].Content.ShouldBe("");
    }

    [Test]
    public void MergeConsecutiveRoles_FirstNullSecondNonNull_MergesCorrectly()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, null),
            new(MessageRole.User, "real content"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe("real content");
    }

    // ── Assistant-assistant merging ───────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_TwoConsecutiveAssistantMessages_Merged()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.Assistant, "part one"),
            new(MessageRole.Assistant, "part two"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(1);
        result[0].Role.ShouldBe(MessageRole.Assistant);
        result[0].Content.ShouldBe("part one\n\npart two");
    }

    // ── System messages are never merged ─────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_TwoConsecutiveSystemMessages_NotMerged()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, "prompt one"),
            new(MessageRole.System, "prompt two"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(2, "system messages must never be merged");
    }

    // ── Tool messages are never merged ────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_TwoConsecutiveToolMessages_NotMerged()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(MessageRole.Tool, "result-a") with { ToolCallId = "tc-1" },
            new ChatMessage(MessageRole.Tool, "result-b") with { ToolCallId = "tc-2" },
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(2, "tool messages must never be merged");
        result[0].ToolCallId.ShouldBe("tc-1");
        result[1].ToolCallId.ShouldBe("tc-2");
    }

    // ── Assistant messages with ToolCalls are never merged ────────────

    [Test]
    public void MergeConsecutiveRoles_AssistantWithToolCalls_NotMergedWithNext()
    {
        var toolCalls = new[] { new ToolCall("tc-1", "some_tool", "{}") };
        var messages = new List<ChatMessage>
        {
            new ChatMessage(MessageRole.Assistant, "calling tool") with { ToolCalls = toolCalls },
            new(MessageRole.Assistant, "follow up"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(2,
            "an assistant message with tool calls must not be merged with the next assistant message");
        result[0].ToolCalls.ShouldBe(toolCalls,
            "tool call metadata on the first message must be preserved");
        result[1].Content.ShouldBe("follow up");
    }

    [Test]
    public void MergeConsecutiveRoles_AssistantWithToolCalls_NotMergedWithPrevious()
    {
        var toolCalls = new[] { new ToolCall("tc-1", "some_tool", "{}") };
        var messages = new List<ChatMessage>
        {
            new(MessageRole.Assistant, "before"),
            new ChatMessage(MessageRole.Assistant, "calling tool") with { ToolCalls = toolCalls },
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(2,
            "an assistant message with tool calls must not be merged with the preceding assistant message");
        result[1].ToolCalls.ShouldBe(toolCalls);
    }

    // ── Alternating roles — no merging ────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_AlternatingUserAssistant_NoMerge()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, "question"),
            new(MessageRole.Assistant, "answer"),
            new(MessageRole.User, "follow-up"),
            new(MessageRole.Assistant, "answer2"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(4, "alternating roles must not be merged");
    }

    // ── Mixed merge and non-merge ─────────────────────────────────────

    [Test]
    public void MergeConsecutiveRoles_MixedPattern_OnlyConsecutiveSameRoleMerged()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, "u1"),
            new(MessageRole.User, "u2"),   // merges with u1
            new(MessageRole.Assistant, "a1"),
            new(MessageRole.Assistant, "a2"), // merges with a1
            new(MessageRole.User, "u3"),
        };

        var result = AgentLoop.MergeConsecutiveRoles(messages);

        result.Count.ShouldBe(3);
        result[0].Role.ShouldBe(MessageRole.User);
        result[0].Content.ShouldBe("u1\n\nu2");
        result[1].Role.ShouldBe(MessageRole.Assistant);
        result[1].Content.ShouldBe("a1\n\na2");
        result[2].Role.ShouldBe(MessageRole.User);
        result[2].Content.ShouldBe("u3");
    }
}
