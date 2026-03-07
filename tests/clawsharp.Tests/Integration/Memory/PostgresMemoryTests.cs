using Clawsharp.Memory.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Create schema by instantiating memory once
        var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                      .UseNpgsql(_connectionString)
                      .Options;
        var factory = new SimpleDbContextFactory<PostgresMemoryContext>(options);
        _ = new PostgresMemory(factory, NullLogger<PostgresMemory>.Instance);

        // Initialize Respawn
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
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
                      .UseNpgsql(_connectionString)
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
}