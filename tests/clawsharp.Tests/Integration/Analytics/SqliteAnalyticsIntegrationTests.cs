using Clawsharp.Analytics;
using Clawsharp.Analytics.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Integration.Analytics;

/// <summary>
/// Integration tests for the SQLite analytics EF Core backend.
/// Verifies auto-migration, round-trip persistence, and field fidelity
/// using real temp database files (no Testcontainers needed).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SqliteAnalyticsIntegrationTests
{
    private string _dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"clawsharp-sqlite-analytics-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        CleanupDb(_dbPath);
    }

    private EfInteractionStore<SqliteAnalyticsContext> CreateStore(bool skipMigration = false)
    {
        var factory = new FileDbContextFactory(_dbPath);
        return new EfInteractionStore<SqliteAnalyticsContext>(
            factory,
            NullLogger<EfInteractionStore<SqliteAnalyticsContext>>.Instance,
            skipMigration: skipMigration);
    }

    // ── Auto-migration tests ──

    [Test]
    public async Task AutoMigration_CreatesDatabase_OnFirstAppend()
    {
        File.Exists(_dbPath).ShouldBeFalse("DB file should not exist before first operation");

        var store = CreateStore(skipMigration: false);
        await store.AppendAsync(MakeRecord());

        File.Exists(_dbPath).ShouldBeTrue("DB file should be created by auto-migration");

        var results = await store.ReadAllAsync();
        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("auto-mig-001");
    }

    [Test]
    public async Task AutoMigration_CreatesDatabase_OnFirstReadAll()
    {
        File.Exists(_dbPath).ShouldBeFalse("DB file should not exist before first operation");

        var store = CreateStore(skipMigration: false);
        var results = await store.ReadAllAsync();

        File.Exists(_dbPath).ShouldBeTrue("DB file should be created by auto-migration on ReadAll");
        results.ShouldBeEmpty();
    }

    // ── Round-trip tests ──

    [Test]
    public async Task AppendAndReadAll_RoundTrips_AllFields()
    {
        var store = CreateStore(skipMigration: false);
        var timestamp = new DateTimeOffset(2026, 3, 11, 14, 30, 0, TimeSpan.Zero);

        var record = new InteractionRecord
        {
            Id = "full-roundtrip",
            SessionId = "session-xyz",
            Channel = "telegram",
            Model = "claude-3-opus",
            UserPrompt = "Explain quantum computing",
            Thinking = "Let me reason through the key concepts...",
            Response = "Quantum computing uses qubits that can exist in superposition.",
            ToolCalls =
            [
                new ToolCallSummary { Name = "web_fetch", ResultLength = 4096 },
                new ToolCallSummary { Name = "file_read", ResultLength = 512 },
            ],
            ToolIterations = 2,
            InputTokens = 1500,
            OutputTokens = 800,
            CacheReadTokens = 300,
            CacheWriteTokens = 150,
            CostUsd = 0.042,
            CacheSavingsUsd = 0.008,
            DurationMs = 3200,
            Timestamp = timestamp,
        };

        await store.AppendAsync(record);
        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(1);
        var r = results[0];

        r.Id.ShouldBe("full-roundtrip");
        r.SessionId.ShouldBe("session-xyz");
        r.Channel.ShouldBe("telegram");
        r.Model.ShouldBe("claude-3-opus");
        r.UserPrompt.ShouldBe("Explain quantum computing");
        r.Thinking.ShouldBe("Let me reason through the key concepts...");
        r.Response.ShouldBe("Quantum computing uses qubits that can exist in superposition.");
        r.ToolIterations.ShouldBe(2);
        r.InputTokens.ShouldBe(1500);
        r.OutputTokens.ShouldBe(800);
        r.CacheReadTokens.ShouldBe(300);
        r.CacheWriteTokens.ShouldBe(150);
        r.CostUsd.ShouldBe(0.042, tolerance: 0.0001);
        r.CacheSavingsUsd.ShouldBe(0.008, tolerance: 0.0001);
        r.DurationMs.ShouldBe(3200);
        r.Timestamp.ShouldBe(timestamp);

        r.ToolCalls.ShouldNotBeNull();
        r.ToolCalls!.Count.ShouldBe(2);
        r.ToolCalls[0].Name.ShouldBe("web_fetch");
        r.ToolCalls[0].ResultLength.ShouldBe(4096);
        r.ToolCalls[1].Name.ShouldBe("file_read");
        r.ToolCalls[1].ResultLength.ShouldBe(512);
    }

    [Test]
    public async Task MultipleRecords_OrderedById()
    {
        var store = CreateStore(skipMigration: false);

        await store.AppendAsync(MakeRecord("rec-alpha", "gpt-4o"));
        await store.AppendAsync(MakeRecord("rec-beta", "claude-3-opus"));
        await store.AppendAsync(MakeRecord("rec-gamma", "gemini-pro"));

        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(3);
        results[0].Id.ShouldBe("rec-alpha");
        results[0].Model.ShouldBe("gpt-4o");
        results[1].Id.ShouldBe("rec-beta");
        results[1].Model.ShouldBe("claude-3-opus");
        results[2].Id.ShouldBe("rec-gamma");
        results[2].Model.ShouldBe("gemini-pro");
    }

    [Test]
    public async Task ToolCalls_SerializedAsJson_RoundTrips()
    {
        var store = CreateStore(skipMigration: false);

        var record = new InteractionRecord
        {
            Id = "complex-tools",
            SessionId = "session-tools",
            Channel = "discord",
            Model = "gpt-4o",
            UserPrompt = "do many things",
            Response = "Done with all tasks",
            ToolCalls =
            [
                new ToolCallSummary { Name = "shell_exec", ResultLength = 2048 },
                new ToolCallSummary { Name = "web_fetch", ResultLength = 8192 },
                new ToolCallSummary { Name = "file_write", ResultLength = 0 },
                new ToolCallSummary { Name = "memory_store", ResultLength = 64 },
            ],
            ToolIterations = 4,
            InputTokens = 3000,
            OutputTokens = 1200,
            CostUsd = 0.05,
            DurationMs = 5000,
            Timestamp = DateTimeOffset.UtcNow,
        };

        await store.AppendAsync(record);
        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(1);
        var tc = results[0].ToolCalls;
        tc.ShouldNotBeNull();
        tc!.Count.ShouldBe(4);
        tc[0].Name.ShouldBe("shell_exec");
        tc[0].ResultLength.ShouldBe(2048);
        tc[1].Name.ShouldBe("web_fetch");
        tc[1].ResultLength.ShouldBe(8192);
        tc[2].Name.ShouldBe("file_write");
        tc[2].ResultLength.ShouldBe(0);
        tc[3].Name.ShouldBe("memory_store");
        tc[3].ResultLength.ShouldBe(64);
    }

    [Test]
    public async Task NullToolCalls_RoundTrips()
    {
        var store = CreateStore(skipMigration: false);

        await store.AppendAsync(MakeRecord("no-tools"));

        var results = await store.ReadAllAsync();
        results.Count.ShouldBe(1);
        results[0].ToolCalls.ShouldBeNull();
        results[0].ToolIterations.ShouldBe(0);
    }

    // ── Helpers ──

    private static InteractionRecord MakeRecord(string id = "auto-mig-001", string model = "gpt-4o") => new()
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

    private static void CleanupDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-journal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
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
