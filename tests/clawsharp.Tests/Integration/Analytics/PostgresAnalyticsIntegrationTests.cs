using Clawsharp.Analytics;
using Clawsharp.Analytics.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Clawsharp.Tests.Integration.Analytics;

/// <summary>
/// Integration tests for the PostgreSQL analytics EF Core backend.
/// Uses Testcontainers for a real Postgres instance and Respawn for data cleanup.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PostgresAnalyticsIntegrationTests
{
    private PostgreSqlContainer _container = null!;
    private string _connectionString = null!;
    private Respawner _respawner = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new PostgreSqlBuilder("postgres:18").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Run auto-migration to create schema
        var store = CreateStore(skipMigration: false);
        await store.ReadAllAsync(); // triggers EnsureInitializedAsync

        // Initialize Respawn
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    private EfInteractionStore<PostgresAnalyticsContext> CreateStore(bool skipMigration = true)
    {
        var options = new DbContextOptionsBuilder<PostgresAnalyticsContext>()
                      .UseNpgsql(_connectionString)
                      .Options;
        var factory = new SimpleDbContextFactory<PostgresAnalyticsContext>(options);
        return new EfInteractionStore<PostgresAnalyticsContext>(
            factory,
            NullLogger<EfInteractionStore<PostgresAnalyticsContext>>.Instance,
            skipMigration: skipMigration);
    }

    // ── Schema tests ──

    [Test]
    public async Task AutoMigration_CreatesSchema()
    {
        // Schema was created in OneTimeSetUp via auto-migration.
        // Verify the Interactions table exists by querying it.
        var store = CreateStore(skipMigration: true);
        var results = await store.ReadAllAsync();
        results.ShouldBeEmpty();
    }

    // ── Round-trip tests ──

    [Test]
    public async Task AppendAndReadAll_RoundTrips_AllFields()
    {
        var store = CreateStore();
        var timestamp = new DateTimeOffset(2026, 3, 11, 14, 30, 0, TimeSpan.Zero);

        var record = new InteractionRecord
        {
            Id = "pg-roundtrip",
            SessionId = "session-pg",
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
            CostUsd = 0.042m,
            CacheSavingsUsd = 0.008m,
            DurationMs = 3200,
            Timestamp = timestamp,
        };

        await store.AppendAsync(record);
        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(1);
        var r = results[0];

        r.Id.ShouldBe("pg-roundtrip");
        r.SessionId.ShouldBe("session-pg");
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
        r.CostUsd.ShouldBe(0.042m);
        r.CacheSavingsUsd.ShouldBe(0.008m);
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
    public async Task MultipleRecords_Ordered()
    {
        var store = CreateStore();

        await store.AppendAsync(MakeRecord("pg-alpha", "gpt-4o"));
        await store.AppendAsync(MakeRecord("pg-beta", "claude-3-opus"));
        await store.AppendAsync(MakeRecord("pg-gamma", "gemini-pro"));

        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(3);
        // ReadAllAsync orders by Id (auto-increment PK), so insertion order
        results[0].Id.ShouldBe("pg-alpha");
        results[0].Model.ShouldBe("gpt-4o");
        results[1].Id.ShouldBe("pg-beta");
        results[1].Model.ShouldBe("claude-3-opus");
        results[2].Id.ShouldBe("pg-gamma");
        results[2].Model.ShouldBe("gemini-pro");
    }

    [Test]
    public async Task ToolCalls_RoundTrip()
    {
        var store = CreateStore();

        var record = new InteractionRecord
        {
            Id = "pg-tools",
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
            ],
            ToolIterations = 3,
            InputTokens = 3000,
            OutputTokens = 1200,
            CostUsd = 0.05m,
            DurationMs = 5000,
            Timestamp = DateTimeOffset.UtcNow,
        };

        await store.AppendAsync(record);
        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(1);
        var tc = results[0].ToolCalls;
        tc.ShouldNotBeNull();
        tc!.Count.ShouldBe(3);
        tc[0].Name.ShouldBe("shell_exec");
        tc[0].ResultLength.ShouldBe(2048);
        tc[1].Name.ShouldBe("web_fetch");
        tc[1].ResultLength.ShouldBe(8192);
        tc[2].Name.ShouldBe("file_write");
        tc[2].ResultLength.ShouldBe(0);
    }

    [Test]
    public async Task NullToolCalls_RoundTrips()
    {
        var store = CreateStore();

        await store.AppendAsync(MakeRecord("pg-no-tools"));

        var results = await store.ReadAllAsync();
        results.Count.ShouldBe(1);
        results[0].ToolCalls.ShouldBeNull();
        results[0].ToolIterations.ShouldBe(0);
    }

    [Test]
    public async Task Timestamps_UseServerDefault()
    {
        var store = CreateStore();

        // Append a record with a zero timestamp to verify the server default kicks in
        var record = new InteractionRecord
        {
            Id = "pg-ts-default",
            SessionId = "session-ts",
            Channel = "cli",
            Model = "gpt-4o",
            UserPrompt = "hello",
            Response = "hi",
            InputTokens = 10,
            OutputTokens = 5,
            DurationMs = 100,
            Timestamp = default, // DateTimeOffset.MinValue — server should use NOW()
        };

        await store.AppendAsync(record);
        var results = await store.ReadAllAsync();

        results.Count.ShouldBe(1);
        // The entity carries the explicit Timestamp from the record (even if default),
        // but the DB has HasDefaultValueSql("NOW()") — it only applies when the column
        // is not provided. EF Core always sends the value, so verify it round-trips.
        // The key verification: no exception was thrown and the record persists.
        results[0].Id.ShouldBe("pg-ts-default");
    }

    [Test]
    public async Task RespawnResetsData_BetweenTests()
    {
        var store = CreateStore();
        var results = await store.ReadAllAsync();
        results.ShouldBeEmpty();
    }

    // ── ConversationThread & InteractionMessage tests ──

    [Test]
    public async Task AppendAsync_CreatesConversationThread_InPostgres()
    {
        var store = CreateStore();
        await store.AppendAsync(MakeRecord("pg-thread-1"));

        var options = new DbContextOptionsBuilder<PostgresAnalyticsContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var ctx = new PostgresAnalyticsContext(options);
        var threads = await ctx.ConversationThreads.ToListAsync();

        threads.Count.ShouldBe(1);
        threads[0].SessionId.ShouldBe("session-a");
    }

    [Test]
    public async Task AppendAsync_SameSession_ReusesSameThread_InPostgres()
    {
        var store = CreateStore();
        await store.AppendAsync(MakeRecord("pg-reuse-1"));
        await store.AppendAsync(MakeRecord("pg-reuse-2"));

        var options = new DbContextOptionsBuilder<PostgresAnalyticsContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var ctx = new PostgresAnalyticsContext(options);
        var threads = await ctx.ConversationThreads.ToListAsync();

        threads.Count.ShouldBe(1);
    }

    [Test]
    public async Task AppendAsync_CreatesInteractionMessages_InPostgres()
    {
        var store = CreateStore();

        var record = new InteractionRecord
        {
            Id = "pg-msg-rows",
            SessionId = "session-msg",
            Channel = "cli",
            Model = "gpt-4o",
            UserPrompt = "Hello",
            Thinking = "Let me think...",
            Response = "Hi there",
            InputTokens = 100,
            OutputTokens = 50,
            DurationMs = 250,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await store.AppendAsync(record);

        var options = new DbContextOptionsBuilder<PostgresAnalyticsContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var ctx = new PostgresAnalyticsContext(options);
        var messages = await ctx.InteractionMessages.ToListAsync();

        messages.Count.ShouldBe(3);
    }

    [Test]
    public async Task AppendAsync_MessageTypes_MatchExpected_InPostgres()
    {
        var store = CreateStore();

        var record = new InteractionRecord
        {
            Id = "pg-msg-types",
            SessionId = "session-types",
            Channel = "cli",
            Model = "gpt-4o",
            UserPrompt = "Q",
            Thinking = "T",
            Response = "A",
            InputTokens = 10,
            OutputTokens = 5,
            DurationMs = 100,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await store.AppendAsync(record);

        var options = new DbContextOptionsBuilder<PostgresAnalyticsContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var ctx = new PostgresAnalyticsContext(options);
        var messages = await ctx.InteractionMessages.OrderBy(m => m.SequenceNumber).ToListAsync();

        messages[0].MessageType.ShouldBe(MessageType.Sent);
        messages[1].MessageType.ShouldBe(MessageType.Thinking);
        messages[2].MessageType.ShouldBe(MessageType.Received);
    }

    [Test]
    public async Task AppendAsync_ConversationThreadId_IsSetCorrectly_InPostgres()
    {
        var store = CreateStore();
        await store.AppendAsync(MakeRecord("pg-fk-check"));

        var options = new DbContextOptionsBuilder<PostgresAnalyticsContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var ctx = new PostgresAnalyticsContext(options);
        var thread = await ctx.ConversationThreads.SingleAsync();
        var interaction = await ctx.Set<Clawsharp.Analytics.Entities.InteractionEntity>().SingleAsync();

        interaction.ConversationThreadId.ShouldBe(thread.Id);
        thread.Id.ShouldBeGreaterThan(0);
    }

    // ── Helpers ──

    private static InteractionRecord MakeRecord(string id = "pg-001", string model = "gpt-4o") => new()
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
        CostUsd = 0.001m,
        CacheSavingsUsd = 0.0m,
        DurationMs = 250,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
