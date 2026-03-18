using Clawsharp.Analytics;
using Clawsharp.Analytics.Entities;
using Clawsharp.Analytics.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Integration.Analytics;

/// <summary>
/// Tests for conversation thread grouping and assignment using SQLite.
/// </summary>
[TestFixture]
public sealed class ConversationThreadTests
{
    private static (EfInteractionStore<SqliteAnalyticsContext> Store, FileDbContextFactory Factory, string DbPath) CreateStore()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clawsharp-thread-{Guid.NewGuid():N}.db");
        var factory = new FileDbContextFactory(dbPath);

        using var initCtx = factory.CreateDbContext();
        initCtx.Database.EnsureCreated();

        var store = new EfInteractionStore<SqliteAnalyticsContext>(
            factory, NullLogger<EfInteractionStore<SqliteAnalyticsContext>>.Instance,
            skipMigration: true);

        return (store, factory, dbPath);
    }

    private static void CleanupDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-journal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
    }

    private static InteractionRecord MakeRecord(string id, string sessionId, string? thinking = null) => new()
    {
        Id = id,
        SessionId = sessionId,
        Channel = "cli",
        Model = "gpt-4o",
        UserPrompt = $"Prompt for {id}",
        Thinking = thinking,
        Response = $"Response for {id}",
        InputTokens = 100,
        OutputTokens = 50,
        DurationMs = 200,
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Test]
    public async Task MultipleInteractions_SameSession_GroupedByThread()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            for (var i = 1; i <= 5; i++)
            {
                await store.AppendAsync(MakeRecord($"rec-{i}", "same-session"));
            }

            using var ctx = factory.CreateDbContext();
            var threads = ctx.ConversationThreads.ToList();
            var interactions = ctx.Set<InteractionEntity>().ToList();

            threads.Count.ShouldBe(1);
            interactions.Count.ShouldBe(5);
            interactions.ShouldAllBe(e => e.ConversationThreadId == threads[0].Id);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ThreadId_IsSequential()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord("rec-1", "session-alpha"));
            await store.AppendAsync(MakeRecord("rec-2", "session-beta"));
            await store.AppendAsync(MakeRecord("rec-3", "session-gamma"));

            using var ctx = factory.CreateDbContext();
            var threads = ctx.ConversationThreads.OrderBy(t => t.Id).ToList();

            threads.Count.ShouldBe(3);
            threads[1].Id.ShouldBeGreaterThan(threads[0].Id);
            threads[2].Id.ShouldBeGreaterThan(threads[1].Id);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task MessageRows_LinkedToCorrectInteraction()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord("rec-1", "session-a"));
            await store.AppendAsync(MakeRecord("rec-2", "session-a"));

            using var ctx = factory.CreateDbContext();
            var interactions = ctx.Set<InteractionEntity>().OrderBy(e => e.Id).ToList();
            var messages = ctx.InteractionMessages.ToList();

            interactions.Count.ShouldBe(2);

            var messagesForFirst = messages.Where(m => m.InteractionId == interactions[0].Id).ToList();
            var messagesForSecond = messages.Where(m => m.InteractionId == interactions[1].Id).ToList();

            messagesForFirst.Count.ShouldBe(2);
            messagesForSecond.Count.ShouldBe(2);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task MessageRows_OrderedBySequenceNumber()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            var record = MakeRecord("rec-1", "session-a", thinking: "Some reasoning");
            await store.AppendAsync(record);

            using var ctx = factory.CreateDbContext();
            var interaction = ctx.Set<InteractionEntity>().Single();
            var messages = ctx.InteractionMessages
                .Where(m => m.InteractionId == interaction.Id)
                .OrderBy(m => m.SequenceNumber)
                .ToList();

            messages.Count.ShouldBe(3);
            messages[0].SequenceNumber.ShouldBe(0);
            messages[0].MessageType.ShouldBe(MessageType.Sent);
            messages[1].SequenceNumber.ShouldBe(1);
            messages[1].MessageType.ShouldBe(MessageType.Thinking);
            messages[2].SequenceNumber.ShouldBe(2);
            messages[2].MessageType.ShouldBe(MessageType.Received);
        }
        finally { CleanupDb(dbPath); }
    }

    private sealed class FileDbContextFactory(string dbPath) : IDbContextFactory<SqliteAnalyticsContext>
    {
        public SqliteAnalyticsContext CreateDbContext()
        {
            var opts = new DbContextOptionsBuilder<SqliteAnalyticsContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            return new SqliteAnalyticsContext(opts);
        }
    }
}
