using Clawsharp.Analytics;
using Clawsharp.Analytics.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Analytics;

/// <summary>
/// Tests the EF Core-backed interaction store using temp SQLite files.
/// </summary>
public sealed class EfInteractionStoreTests
{
    private static (EfInteractionStore<SqliteAnalyticsContext> Store, string DbPath) CreateStore()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clawsharp-ef-analytics-{Guid.NewGuid():N}.db");
        var factory = new FileDbContextFactory(dbPath);

        using var initCtx = factory.CreateDbContext();
        initCtx.Database.EnsureCreated();

        var store = new EfInteractionStore<SqliteAnalyticsContext>(
            factory, NullLogger<EfInteractionStore<SqliteAnalyticsContext>>.Instance,
            skipMigration: true);

        return (store, dbPath);
    }

    private static void CleanupDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-journal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
    }

    private static InteractionRecord MakeRecord(string id = "test-001", string model = "gpt-4o") => new()
    {
        Id = id,
        SessionId = "session-a",
        Channel = "cli",
        Model = model,
        UserPrompt = "Hello",
        Response = "Hi there",
        InputTokens = 100,
        OutputTokens = 50,
        CacheReadTokens = 0,
        CacheWriteTokens = 0,
        CostUsd = 0.001,
        CacheSavingsUsd = 0.0,
        DurationMs = 250,
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Test]
    public async Task AppendAsync_SingleRecord_ReadAllReturnsIt()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord());
            var results = await store.ReadAllAsync();

            results.Count.ShouldBe(1);
            results[0].Id.ShouldBe("test-001");
            results[0].SessionId.ShouldBe("session-a");
            results[0].Channel.ShouldBe("cli");
            results[0].Model.ShouldBe("gpt-4o");
            results[0].UserPrompt.ShouldBe("Hello");
            results[0].Response.ShouldBe("Hi there");
            results[0].InputTokens.ShouldBe(100);
            results[0].OutputTokens.ShouldBe(50);
            results[0].CostUsd.ShouldBe(0.001, tolerance: 0.0001);
            results[0].DurationMs.ShouldBe(250);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task AppendAsync_MultipleRecords_ReadAllReturnsInOrder()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord("rec-1", "gpt-4o"));
            await store.AppendAsync(MakeRecord("rec-2", "claude-3-opus"));
            await store.AppendAsync(MakeRecord("rec-3", "gemini-pro"));

            var results = await store.ReadAllAsync();

            results.Count.ShouldBe(3);
            results[0].Id.ShouldBe("rec-1");
            results[0].Model.ShouldBe("gpt-4o");
            results[1].Id.ShouldBe("rec-2");
            results[1].Model.ShouldBe("claude-3-opus");
            results[2].Id.ShouldBe("rec-3");
            results[2].Model.ShouldBe("gemini-pro");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ReadAllAsync_EmptyDb_ReturnsEmptyList()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            var results = await store.ReadAllAsync();
            results.ShouldBeEmpty();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ToolCalls_RoundTripsCorrectly()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            var record = new InteractionRecord
            {
                Id = "tool-test",
                SessionId = "session-b",
                Channel = "telegram",
                Model = "claude-3-opus",
                UserPrompt = "list files",
                Response = "Here are the files",
                ToolCalls = [new ToolCallSummary { Name = "list_files", ResultLength = 1024 }],
                ToolIterations = 1,
                InputTokens = 200,
                OutputTokens = 100,
                CacheReadTokens = 50,
                CacheWriteTokens = 10,
                CostUsd = 0.005,
                CacheSavingsUsd = 0.001,
                DurationMs = 500,
                Timestamp = DateTimeOffset.UtcNow,
            };

            await store.AppendAsync(record);
            var results = await store.ReadAllAsync();

            results.Count.ShouldBe(1);
            results[0].ToolCalls.ShouldNotBeNull();
            results[0].ToolCalls!.Count.ShouldBe(1);
            results[0].ToolCalls![0].Name.ShouldBe("list_files");
            results[0].ToolCalls![0].ResultLength.ShouldBe(1024);
            results[0].ToolIterations.ShouldBe(1);
            results[0].CacheReadTokens.ShouldBe(50);
            results[0].CacheWriteTokens.ShouldBe(10);
            results[0].CacheSavingsUsd.ShouldBe(0.001, tolerance: 0.0001);
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task ThinkingContent_RoundTrips()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            var record = new InteractionRecord
            {
                Id = "thinking-test",
                SessionId = "session-a",
                Channel = "cli",
                Model = "gpt-4o",
                UserPrompt = "Hello",
                Thinking = "Let me reason about this...",
                Response = "Hi there",
                InputTokens = 100,
                OutputTokens = 50,
                DurationMs = 250,
                Timestamp = DateTimeOffset.UtcNow,
            };
            await store.AppendAsync(record);

            var results = await store.ReadAllAsync();

            results.Count.ShouldBe(1);
            results[0].Thinking.ShouldBe("Let me reason about this...");
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task NullToolCalls_RoundTripsAsNull()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            await store.AppendAsync(MakeRecord("no-tools"));
            var results = await store.ReadAllAsync();

            results.Count.ShouldBe(1);
            results[0].ToolCalls.ShouldBeNull();
        }
        finally { CleanupDb(dbPath); }
    }

    [Test]
    public async Task MultipleToolCalls_RoundTrip()
    {
        var (store, dbPath) = CreateStore();
        try
        {
            var record = new InteractionRecord
            {
                Id = "multi-tools",
                SessionId = "session-c",
                Channel = "discord",
                Model = "gpt-4o",
                UserPrompt = "complex task",
                Response = "Done",
                ToolCalls =
                [
                    new ToolCallSummary { Name = "web_fetch", ResultLength = 2048 },
                    new ToolCallSummary { Name = "shell_exec", ResultLength = 512 },
                    new ToolCallSummary { Name = "file_write", ResultLength = 0 },
                ],
                ToolIterations = 3,
                InputTokens = 500,
                OutputTokens = 200,
                CacheReadTokens = 100,
                CacheWriteTokens = 50,
                CostUsd = 0.01,
                CacheSavingsUsd = 0.003,
                DurationMs = 1500,
                Timestamp = DateTimeOffset.UtcNow,
            };

            await store.AppendAsync(record);
            var results = await store.ReadAllAsync();

            results[0].ToolCalls!.Count.ShouldBe(3);
            results[0].ToolCalls![0].Name.ShouldBe("web_fetch");
            results[0].ToolCalls![1].Name.ShouldBe("shell_exec");
            results[0].ToolCalls![2].Name.ShouldBe("file_write");
        }
        finally { CleanupDb(dbPath); }
    }

    private sealed class FileDbContextFactory : IDbContextFactory<SqliteAnalyticsContext>
    {
        private readonly string _dbPath;
        public FileDbContextFactory(string dbPath) => _dbPath = dbPath;

        public SqliteAnalyticsContext CreateDbContext()
        {
            var opts = new DbContextOptionsBuilder<SqliteAnalyticsContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
            return new SqliteAnalyticsContext(opts);
        }
    }
}
