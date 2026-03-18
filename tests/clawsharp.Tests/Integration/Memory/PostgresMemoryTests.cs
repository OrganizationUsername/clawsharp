using Clawsharp.Memory.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Respawn;
using Testcontainers.PostgreSql;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed class PostgresMemoryTests
{
    private PostgreSqlContainer _container = null!;

    private string _connectionString = null!;

    private Respawner _respawner = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg18-trixie").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Init schema — must call a method to trigger lazy migration before Respawn scans tables
        var memory = CreateMemory();
        await memory.AppendFactAsync("schema init");
        await memory.ClearAsync();

        // Initialize Respawn
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore =
            [
                new Respawn.Graph.Table("__EFMigrationsHistory"),
                new Respawn.Graph.Table("History"),
            ],
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

    private PostgresMemory CreateMemory()
    {
        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString, o => o.UseVector())
                      .Options;
        var factory = new SimpleDbContextFactory<PostgresMemoryContext>(options);
        return new PostgresMemory(factory, NullLogger<PostgresMemory>.Instance);
    }

    [Test]
    public async Task GetContextAsync_NoFacts_ReturnsNull()
    {
        var memory = CreateMemory();
        var result = await memory.GetContextAsync();
        result.ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_StoresFact()
    {
        var memory = CreateMemory();

        await memory.AppendFactAsync("user prefers dark mode");

        var result = await memory.GetContextAsync();
        result.ShouldNotBeNull();
        result!.ShouldContain("user prefers dark mode");
    }

    [Test]
    public async Task GetContextAsync_ReturnsMemoryHeader()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("some fact");

        var result = await memory.GetContextAsync();

        result!.ShouldStartWith("## Memory");
    }

    [Test]
    public async Task AppendHistoryAsync_StoresHistory()
    {
        var memory = CreateMemory();

        await memory.AppendHistoryAsync("User asked about weather");

        // History is not part of GetContextAsync -- just verify no exception
        var result = await memory.GetContextAsync();
        // facts = 0 so null
        result.ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllPersist()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("fact alpha");
        await memory.AppendFactAsync("fact beta");
        await memory.AppendFactAsync("fact gamma");

        var result = await memory.GetContextAsync();

        result.ShouldNotBeNull();
        result!.ShouldContain("fact alpha");
        result!.ShouldContain("fact beta");
        result!.ShouldContain("fact gamma");
    }

    [Test]
    public async Task SearchAsync_NoFacts_ReturnsEmpty()
    {
        var memory = CreateMemory();
        var results = await memory.SearchAsync("anything");
        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_ILikeFallback_FindsMatch()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user likes pizza");
        await memory.AppendFactAsync("user dislikes broccoli");

        var results = await memory.SearchAsync("pizza");

        results.ShouldNotBeEmpty();
        results[0].ShouldContain("pizza");
    }

    [Test]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user likes pizza");

        var results = await memory.SearchAsync("xyzzy");

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_RespectsNLimit()
    {
        var memory = CreateMemory();
        for (var i = 0; i < 10; i++)
        {
            await memory.AppendFactAsync($"fact about cats number {i}");
        }

        var results = await memory.SearchAsync("cats", n: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Test]
    public async Task GetContextAsync_ReturnsAtMost50Facts()
    {
        var memory = CreateMemory();
        for (var i = 0; i < 60; i++)
        {
            await memory.AppendFactAsync($"fact number {i}");
        }

        var result = await memory.GetContextAsync();
        var lines = result!.Split('\n').Count(l => l.StartsWith("- "));
        lines.ShouldBe(50);
    }

    [Test]
    public async Task RespawnResetsData_BetweenTests()
    {
        var memory = CreateMemory();
        var result = await memory.GetContextAsync();
        result.ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_FactPersistedInDatabase()
    {
        var memory = CreateMemory();

        await memory.AppendFactAsync("user prefers vim keybindings");

        // Query the Facts table directly via DbContext
        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString, o => o.UseVector())
                      .Options;
        await using var ctx = new PostgresMemoryContext(options);
        var facts = await ctx.Facts.ToListAsync();

        facts.Count.ShouldBe(1);
        facts[0].Content.ShouldBe("user prefers vim keybindings");
        facts[0].CreatedAt.ShouldNotBe(default);
        facts[0].AccessCount.ShouldBe(0);
    }

    [Test]
    public async Task AppendHistoryAsync_HistoryPersistedInDatabase()
    {
        var memory = CreateMemory();

        await memory.AppendHistoryAsync("User discussed deployment strategies");

        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString, o => o.UseVector())
                      .Options;
        await using var ctx = new PostgresMemoryContext(options);
        var history = await ctx.History.ToListAsync();

        history.Count.ShouldBe(1);
        history[0].Summary.ShouldBe("User discussed deployment strategies");
        history[0].Ts.ShouldNotBe(default);
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllHaveUniqueIds()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("fact one");
        await memory.AppendFactAsync("fact two");
        await memory.AppendFactAsync("fact three");

        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString, o => o.UseVector())
                      .Options;
        await using var ctx = new PostgresMemoryContext(options);
        var facts = await ctx.Facts.OrderBy(f => f.Id).ToListAsync();

        facts.Count.ShouldBe(3);
        facts.Select(f => f.Id).Distinct().Count().ShouldBe(3);
        // Auto-increment PKs should be sequential
        facts[1].Id.ShouldBeGreaterThan(facts[0].Id);
        facts[2].Id.ShouldBeGreaterThan(facts[1].Id);
    }

    [Test]
    public async Task ListFactsAsync_ReturnsFacts_WithCorrectFields()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("the sky is blue");
        await memory.AppendFactAsync("water is wet");

        var facts = await memory.ListFactsAsync();

        facts.Count.ShouldBe(2);
        foreach (var fact in facts)
        {
            fact.Id.ShouldBeGreaterThan(0);
            fact.Content.ShouldNotBeNullOrWhiteSpace();
            fact.CreatedAt.ShouldNotBe(default);
        }

        facts.ShouldContain(f => f.Content == "the sky is blue");
        facts.ShouldContain(f => f.Content == "water is wet");
    }

    [Test]
    public async Task SearchAsync_TsQuerySearch_FindsRelevantFacts()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("the quick brown fox jumps over the lazy dog");
        await memory.AppendFactAsync("cats are independent animals");
        await memory.AppendFactAsync("the fox is a clever creature");

        // tsquery should match "fox" via full-text search
        var results = await memory.SearchAsync("fox");

        results.ShouldNotBeEmpty();
        results.ShouldAllBe(r => r.Contains("fox", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task ClearAsync_RemovesAllFacts_PreservesHistory()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("fact to delete");
        await memory.AppendFactAsync("another fact to delete");
        await memory.AppendHistoryAsync("history entry preserved");

        await memory.ClearAsync();

        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString, o => o.UseVector())
                      .Options;
        await using var ctx = new PostgresMemoryContext(options);
        var facts = await ctx.Facts.ToListAsync();
        var history = await ctx.History.ToListAsync();

        facts.ShouldBeEmpty();
        // History is WORM — preserved across clears (count includes entries from other tests)
        history.ShouldContain(h => h.Summary == "history entry preserved");
    }

    [Test]
    public async Task AppendHistoryAsync_MultipleEntries_AllPreservedAcrossClear()
    {
        var memory = CreateMemory();
        await memory.AppendHistoryAsync("first summary");
        await memory.AppendHistoryAsync("second summary");

        // Clear should not affect history (WORM semantics)
        await memory.ClearAsync();

        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString, o => o.UseVector())
                      .Options;
        await using var ctx = new PostgresMemoryContext(options);
        var history = await ctx.History.OrderBy(h => h.Id).ToListAsync();

        history.Count.ShouldBeGreaterThanOrEqualTo(2);
        history.ShouldContain(h => h.Summary == "first summary");
        history.ShouldContain(h => h.Summary == "second summary");
    }
}