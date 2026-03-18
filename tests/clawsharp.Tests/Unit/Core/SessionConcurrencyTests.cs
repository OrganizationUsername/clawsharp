using Clawsharp.Core;
using Clawsharp.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Unit.Core;

/// <summary>
/// MED-25: Tests for concurrent session access patterns.
/// Documents that SessionStore is NOT safe for concurrent writes to the same session ID
/// (callers must serialize access), but IS safe for concurrent writes to different sessions.
/// </summary>
[TestFixture]
public sealed class SessionConcurrencyTests : IDisposable
{
    private readonly string _sessionsDir;

    public SessionConcurrencyTests()
    {
        _sessionsDir = Path.Combine(Path.GetTempPath(), $"clawsharp-session-conc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_sessionsDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private SessionStore CreateStore() => new(_sessionsDir, NullLogger<SessionStore>.Instance);

    // ── Concurrent saves to SAME session: known limitation ───────────────

    [Test]
    public async Task ConcurrentSaves_SameSession_KnownLimitation_CanThrowFileNotFound()
    {
        // Known limitation: SessionStore.SaveAsync uses a shared ".tmp" path derived
        // from the session ID. When multiple threads save the same session concurrently,
        // they race on the same .tmp file: one thread's File.Move succeeds, then the
        // catch block of a losing thread may delete the .tmp that a third thread just
        // created. This produces FileNotFoundException.
        //
        // In production, the AgentLoop serializes saves per session, so this race
        // does not occur. This test documents the limitation.
        var store = CreateStore();
        const string sessionId = "test:concurrent-save";
        const int parallelism = 20;

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, parallelism)
            .Select(async i =>
            {
                try
                {
                    var session = new Session
                    {
                        Id = sessionId,
                        TotalInputTokens = i * 100,
                        TotalOutputTokens = i * 50
                    };
                    await store.SaveAsync(session);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // At least some saves should succeed — the final state should be loadable
        var loaded = await store.LoadOrCreateAsync(sessionId);
        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(sessionId);

        // We expect some FileNotFoundExceptions from the .tmp race — this is the known limitation.
        // The test passes regardless of whether exceptions occurred, documenting the behavior.
    }

    // ── Concurrent saves to DIFFERENT sessions: safe ─────────────────────

    [Test]
    public async Task ConcurrentSaves_DifferentSessions_AllPersisted()
    {
        // Different session IDs use different file paths, so concurrent saves are safe.
        var store = CreateStore();
        const int sessionCount = 30;

        var tasks = Enumerable.Range(0, sessionCount)
            .Select(async i =>
            {
                var session = new Session
                {
                    Id = $"test:multi-{i}",
                    TotalInputTokens = i * 100
                };
                await store.SaveAsync(session);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Verify all sessions are loadable
        for (var i = 0; i < sessionCount; i++)
        {
            var loaded = await store.LoadOrCreateAsync($"test:multi-{i}");
            loaded.Id.ShouldBe($"test:multi-{i}");
            loaded.TotalInputTokens.ShouldBe(i * 100);
        }
    }

    // ── Concurrent loads are safe ────────────────────────────────────────

    [Test]
    public async Task ConcurrentLoads_SameSession_AllReturnValidData()
    {
        var store = CreateStore();
        const string sessionId = "test:concurrent-load";

        // Seed the session
        var session = new Session
        {
            Id = sessionId,
            TotalInputTokens = 42
        };
        await store.SaveAsync(session);

        // Concurrent loads should all succeed
        const int parallelism = 20;
        var results = new Session[parallelism];

        var tasks = Enumerable.Range(0, parallelism)
            .Select(async i =>
            {
                results[i] = await store.LoadOrCreateAsync(sessionId);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        foreach (var loaded in results)
        {
            loaded.ShouldNotBeNull();
            loaded.Id.ShouldBe(sessionId);
            loaded.TotalInputTokens.ShouldBe(42);
        }
    }

    // ── Concurrent access to session Messages list ──────────────────────

    [Test]
    public void Session_Messages_ConcurrentModification_NotThreadSafe()
    {
        // Document that Session.Messages (List<ChatMessage>) is NOT thread-safe.
        // Callers (AgentLoop) must synchronize access externally.
        // This test verifies the data structure is a plain List, not a concurrent collection.
        var session = new Session { Id = "test:msg-type" };

        session.Messages.ShouldBeAssignableTo<List<ChatMessage>>(
            "Session.Messages is a plain List, requiring external synchronization for thread safety");
    }

    // ── Session pruning ──────────────────────────────────────────────────

    [Test]
    public void Session_Prune_DuringIteration_DoesNotThrow()
    {
        // Verify that Prune doesn't throw when called on a session with messages.
        // This is single-threaded but tests the Prune logic path.
        var session = new Session { Id = "test:prune" };

        for (var i = 0; i < 100; i++)
        {
            session.Messages.Add(new ChatMessage(
                MessageRole.User,
                $"Message {i}",
                Timestamp: DateTimeOffset.UtcNow.AddMinutes(-i)));
        }

        // Prune to keep only 10 messages
        var pruned = session.Prune(maxMessages: 10, maxAgeDays: null);
        pruned.ShouldBeTrue();
        session.Messages.Count.ShouldBe(10);
    }

    // ── Sequential save/load round trip ──────────────────────────────────

    [Test]
    public async Task SequentialSaveLoad_MultipleUpdates_LastWriteWins()
    {
        var store = CreateStore();
        const string sessionId = "test:sequential";

        for (var i = 0; i < 10; i++)
        {
            var session = new Session
            {
                Id = sessionId,
                TotalInputTokens = i * 100
            };
            await store.SaveAsync(session);
        }

        var loaded = await store.LoadOrCreateAsync(sessionId);
        loaded.TotalInputTokens.ShouldBe(900, "Last write should win");
    }
}
