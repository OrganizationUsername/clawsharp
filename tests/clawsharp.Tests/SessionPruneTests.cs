namespace Clawsharp.Tests;

using Core;
using Core.Sessions;

public sealed class SessionPruneTests
{
    private static Session CreateSession(params ChatMessage[] messages)
    {
        return new Session { Id = "test", Messages = [..messages] };
    }

    private static ChatMessage Msg(MessageRole role, string text, DateTimeOffset? ts = null)
    {
        return new ChatMessage(role, text, Timestamp: ts);
    }

    [Test]
    public void Prune_NoLimits_DoesNothing()
    {
        var session = CreateSession(
            Msg(MessageRole.System, "sys"),
            Msg(MessageRole.User, "u1"),
            Msg(MessageRole.Assistant, "a1"));

        var removed = session.Prune(null, null);

        removed.ShouldBeFalse();
        session.Messages.Count.ShouldBe(3);
    }

    [Test]
    public void Prune_MaxMessages_TrimsOldestNonSystem()
    {
        var session = CreateSession(
            Msg(MessageRole.System, "sys"),
            Msg(MessageRole.User, "u1", DateTimeOffset.UtcNow),
            Msg(MessageRole.Assistant, "a1", DateTimeOffset.UtcNow),
            Msg(MessageRole.User, "u2", DateTimeOffset.UtcNow),
            Msg(MessageRole.Assistant, "a2", DateTimeOffset.UtcNow));

        var removed = session.Prune(maxMessages: 2, maxAgeDays: null);

        removed.ShouldBeTrue();
        // System + 2 newest non-system messages
        session.Messages.Count.ShouldBe(3);
        session.Messages[0].Role.ShouldBe(MessageRole.System);
        session.Messages[1].Content.ShouldBe("u2");
        session.Messages[2].Content.ShouldBe("a2");
    }

    [Test]
    public void Prune_MaxMessages_KeepsSystemEvenIfOnlyMessage()
    {
        var session = CreateSession(
            Msg(MessageRole.System, "sys"));

        var removed = session.Prune(maxMessages: 2, maxAgeDays: null);

        removed.ShouldBeFalse();
        session.Messages.Count.ShouldBe(1);
    }

    [Test]
    public void Prune_MaxAgeDays_RemovesOldMessages()
    {
        var old = DateTimeOffset.UtcNow - TimeSpan.FromDays(10);
        var recent = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        var session = CreateSession(
            Msg(MessageRole.System, "sys"),
            Msg(MessageRole.User, "old1", old),
            Msg(MessageRole.Assistant, "old2", old),
            Msg(MessageRole.User, "new1", recent),
            Msg(MessageRole.Assistant, "new2", recent));

        var removed = session.Prune(maxMessages: null, maxAgeDays: 7);

        removed.ShouldBeTrue();
        session.Messages.Count.ShouldBe(3);
        session.Messages[0].Role.ShouldBe(MessageRole.System);
        session.Messages[1].Content.ShouldBe("new1");
        session.Messages[2].Content.ShouldBe("new2");
    }

    [Test]
    public void Prune_MaxAgeDays_LegacyNullTimestampTreatedAsOld()
    {
        var recent = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        var session = CreateSession(
            Msg(MessageRole.System, "sys"),
            Msg(MessageRole.User, "legacy", ts: null),
            Msg(MessageRole.User, "new1", recent));

        var removed = session.Prune(maxMessages: null, maxAgeDays: 7);

        removed.ShouldBeTrue();
        session.Messages.Count.ShouldBe(2);
        session.Messages[1].Content.ShouldBe("new1");
    }

    [Test]
    public void Prune_SystemMessagesNeverRemoved()
    {
        var old = DateTimeOffset.UtcNow - TimeSpan.FromDays(100);

        var session = CreateSession(
            Msg(MessageRole.System, "sys", old),
            Msg(MessageRole.User, "u1", old));

        session.Prune(maxMessages: null, maxAgeDays: 1);

        session.Messages.Count.ShouldBe(1);
        session.Messages[0].Role.ShouldBe(MessageRole.System);
    }

    [Test]
    public void Prune_BothLimits_AppliesAgeFirst_ThenCount()
    {
        var old = DateTimeOffset.UtcNow - TimeSpan.FromDays(10);
        var recent = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        var session = CreateSession(
            Msg(MessageRole.System, "sys"),
            Msg(MessageRole.User, "old1", old),
            Msg(MessageRole.User, "r1", recent),
            Msg(MessageRole.Assistant, "r2", recent),
            Msg(MessageRole.User, "r3", recent));

        // Age prune removes old1, then count prune keeps only 2 non-system
        var removed = session.Prune(maxMessages: 2, maxAgeDays: 7);

        removed.ShouldBeTrue();
        // System + 2 newest non-system
        session.Messages.Count.ShouldBe(3);
        session.Messages[0].Role.ShouldBe(MessageRole.System);
        session.Messages[1].Content.ShouldBe("r2");
        session.Messages[2].Content.ShouldBe("r3");
    }

    [Test]
    public void Prune_UnderLimit_DoesNothing()
    {
        var recent = DateTimeOffset.UtcNow;
        var session = CreateSession(
            Msg(MessageRole.System, "sys"),
            Msg(MessageRole.User, "u1", recent));

        var removed = session.Prune(maxMessages: 10, maxAgeDays: 30);

        removed.ShouldBeFalse();
        session.Messages.Count.ShouldBe(2);
    }
}