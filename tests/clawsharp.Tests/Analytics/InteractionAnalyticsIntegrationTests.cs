using Clawsharp.Analytics;
using Clawsharp.Analytics.Sqlite;
using Clawsharp.Config.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Analytics;

/// <summary>
/// End-to-end integration tests verifying that interaction analytics recording
/// flows through the AgentLoop correctly for both JSONL and SQLite EF Core backends.
/// </summary>
[TestFixture]
public sealed class InteractionAnalyticsIntegrationTests
{
    // ── Test A: JSONL backend records interaction end-to-end ──

    [Test]
    public async Task AgentLoop_WithJsonlBackend_RecordsInteractionEndToEnd()
    {
        var jsonlDir = Path.Combine(Path.GetTempPath(), $"clawsharp-analytics-test-{Guid.NewGuid():N}");
        var jsonlPath = Path.Combine(jsonlDir, "interactions.jsonl");
        var store = new InteractionStorage(jsonlPath);

        try
        {
            using var harness = new AgentLoopTestHarness(
                analyticsConfig: new AnalyticsConfig { Enabled = true },
                interactionStore: store);

            harness.Provider.EnqueueTextStream("Hello", " world");

            await harness.ProcessAsync("test prompt");

            // The analytics recording happens in a fire-and-forget Task.Run inside
            // DispatchToProviderAsync — wait for it to complete.
            await Task.Delay(500);

            var records = await store.ReadAllAsync();

            records.Count.ShouldBe(1);
            var record = records[0];

            record.UserPrompt.ShouldBe("test prompt");
            record.Response.ShouldContain("Hello world");
            record.Channel.ShouldBe("telegram");
            record.Model.ShouldBe("test-model");
            record.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
            record.Timestamp.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
            record.SessionId.ShouldNotBeNullOrEmpty();
            record.Id.ShouldNotBeNullOrEmpty();
        }
        finally
        {
            try { Directory.Delete(jsonlDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── Test B: SQLite EF Core backend records interaction end-to-end ──

    [Test]
    public async Task AgentLoop_WithSqliteBackend_RecordsInteractionEndToEnd()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"clawsharp-analytics-test-{Guid.NewGuid():N}.db");

        try
        {
            var factory = new TestDbContextFactory(dbPath);

            using (var initCtx = factory.CreateDbContext())
            {
                await initCtx.Database.EnsureCreatedAsync();
            }

            var store = new EfInteractionStore<SqliteAnalyticsContext>(
                factory, NullLogger<EfInteractionStore<SqliteAnalyticsContext>>.Instance,
                skipMigration: true);

            using var harness = new AgentLoopTestHarness(
                analyticsConfig: new AnalyticsConfig { Enabled = true, Backend = "sqlite" },
                interactionStore: store);

            harness.Provider.EnqueueTextStream("Hello", " world");

            await harness.ProcessAsync("test prompt");

            // Wait for fire-and-forget analytics recording.
            await Task.Delay(500);

            var records = await store.ReadAllAsync();

            records.Count.ShouldBe(1);
            var record = records[0];

            record.UserPrompt.ShouldBe("test prompt");
            record.Response.ShouldContain("Hello world");
            record.Channel.ShouldBe("telegram");
            record.Model.ShouldBe("test-model");
            record.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
            record.Timestamp.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
            record.SessionId.ShouldNotBeNullOrEmpty();
            record.Id.ShouldNotBeNullOrEmpty();
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    // ── Test C: Analytics disabled does not record ──

    [Test]
    public async Task AgentLoop_WithAnalyticsDisabled_DoesNotRecord()
    {
        var jsonlDir = Path.Combine(Path.GetTempPath(), $"clawsharp-analytics-test-{Guid.NewGuid():N}");
        var jsonlPath = Path.Combine(jsonlDir, "interactions.jsonl");
        var store = new InteractionStorage(jsonlPath);

        try
        {
            // Default: no analyticsConfig => analytics is disabled (_analyticsEnabled = false)
            using var harness = new AgentLoopTestHarness(interactionStore: store);

            harness.Provider.EnqueueTextStream("Hello", " world");

            await harness.ProcessAsync("test prompt");

            // Wait to give any potential background task time to run (it shouldn't).
            await Task.Delay(500);

            var records = await store.ReadAllAsync();
            records.ShouldBeEmpty();
        }
        finally
        {
            try { Directory.Delete(jsonlDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── Test D: Tool calls are recorded in interaction summaries ──

    [Test]
    public async Task AgentLoop_WithToolCalls_RecordsToolCallSummaries()
    {
        var jsonlDir = Path.Combine(Path.GetTempPath(), $"clawsharp-analytics-test-{Guid.NewGuid():N}");
        var jsonlPath = Path.Combine(jsonlDir, "interactions.jsonl");
        var store = new InteractionStorage(jsonlPath);

        try
        {
            using var harness = new AgentLoopTestHarness(
                analyticsConfig: new AnalyticsConfig { Enabled = true },
                interactionStore: store);

            harness.Tools.AddDefinition("shell", "run shell command",
                """{"type":"object","properties":{"command":{"type":"string"}}}""");
            harness.Tools.EnqueueResult("shell", "file1.txt\nfile2.txt");

            // First stream: tool call
            harness.Provider.EnqueueToolCallStream("call_1", "shell", """{"command":"ls"}""");
            // Second stream: final text response after tool result
            harness.Provider.EnqueueTextStream("I found two files.");

            await harness.ProcessAsync("List files");

            // Wait for fire-and-forget analytics recording.
            await Task.Delay(500);

            var records = await store.ReadAllAsync();

            records.Count.ShouldBe(1);
            var record = records[0];

            record.UserPrompt.ShouldBe("List files");
            record.Response.ShouldContain("I found two files.");
            record.ToolCalls.ShouldNotBeNull();
            record.ToolCalls!.Count.ShouldBe(1);
            record.ToolCalls[0].Name.ShouldBe("shell");
            record.ToolIterations.ShouldBe(1);
        }
        finally
        {
            try { Directory.Delete(jsonlDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── Helpers ──

    private static void CleanupDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-journal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-wal"); } catch { /* best-effort */ }
        try { File.Delete(dbPath + "-shm"); } catch { /* best-effort */ }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<SqliteAnalyticsContext>
    {
        private readonly string _dbPath;
        public TestDbContextFactory(string dbPath) => _dbPath = dbPath;

        public SqliteAnalyticsContext CreateDbContext()
        {
            var opts = new DbContextOptionsBuilder<SqliteAnalyticsContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
            return new SqliteAnalyticsContext(opts);
        }
    }
}
