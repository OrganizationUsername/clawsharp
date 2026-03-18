using Clawsharp.Analytics;
using Clawsharp.Analytics.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Integration.Analytics;

/// <summary>
/// Focused tests on interaction message content persistence using SQLite.
/// </summary>
[TestFixture]
public sealed class InteractionMessagePersistenceTests
{
    private static (EfInteractionStore<SqliteAnalyticsContext> Store, FileDbContextFactory Factory, string DbPath) CreateStore()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clawsharp-msgpersist-{Guid.NewGuid():N}.db");
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

    private static InteractionRecord MakeRecord(
        string userPrompt = "Hello",
        string response = "Hi there",
        string? thinking = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = "session-persist",
        Channel = "cli",
        Model = "gpt-4o",
        UserPrompt = userPrompt,
        Thinking = thinking,
        Response = response,
        InputTokens = 100,
        OutputTokens = 50,
        DurationMs = 200,
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Test]
    public async Task SentMessage_StoresUserPrompt()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord(userPrompt: "What is the meaning of life?"));

            using var ctx = factory.CreateDbContext();
            var sent = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Sent);

            sent.Content.ShouldBe("What is the meaning of life?");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ReceivedMessage_StoresResponse()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord(response: "42, of course."));

            using var ctx = factory.CreateDbContext();
            var received = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Received);

            received.Content.ShouldBe("42, of course.");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ThinkingMessage_StoresThinking()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord(thinking: "Deep philosophical reasoning..."));

            using var ctx = factory.CreateDbContext();
            var thinkingMsg = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Thinking);

            thinkingMsg.Content.ShouldBe("Deep philosophical reasoning...");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task NullThinking_OnlyCreates2Rows()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord(thinking: null));

            using var ctx = factory.CreateDbContext();
            var messages = ctx.InteractionMessages.ToList();

            messages.Count.ShouldBe(2);
            messages.ShouldNotContain(m => m.MessageType == MessageType.Thinking);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task EmptyThinking_OnlyCreates2Rows()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord(thinking: ""));

            using var ctx = factory.CreateDbContext();
            var messages = ctx.InteractionMessages.ToList();

            messages.Count.ShouldBe(2);
            messages.ShouldNotContain(m => m.MessageType == MessageType.Thinking);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task LargeContent_PersistsCompletely()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            var largePrompt = new string('A', 10_000) + " end";
            var largeResponse = new string('B', 12_000) + " done";

            await store.AppendAsync(MakeRecord(userPrompt: largePrompt, response: largeResponse));

            using var ctx = factory.CreateDbContext();
            var sent = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Sent);
            var received = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Received);

            sent.Content.ShouldBe(largePrompt);
            sent.Content.Length.ShouldBe(10_004);
            received.Content.ShouldBe(largeResponse);
            received.Content.Length.ShouldBe(12_005);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task SpecialCharacters_InContent_Persist()
    {
        var (store, factory, dbPath) = CreateStore();
        try
        {
            const string unicodePrompt = "Hello \ud83d\ude80 world \u00e9\u00e0\u00fc \u4f60\u597d \u0410\u0411\u0412 \u2603\ufe0f";
            const string specialResponse = "Line1\nLine2\r\nLine3\t\"quoted\" 'single' \\backslash\\ null\0char";

            await store.AppendAsync(MakeRecord(userPrompt: unicodePrompt, response: specialResponse));

            using var ctx = factory.CreateDbContext();
            var sent = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Sent);
            var received = ctx.InteractionMessages.Single(m => m.MessageType == MessageType.Received);

            sent.Content.ShouldBe(unicodePrompt);
            received.Content.ShouldBe(specialResponse);
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
