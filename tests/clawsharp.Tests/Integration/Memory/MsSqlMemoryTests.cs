using Clawsharp.Memory.MsSql;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Respawn;
using Testcontainers.MsSql;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed class MsSqlMemoryTests
{
    private MsSqlContainer _container = null!;

    private string _connectionString = null!;

    private Respawner _respawner = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Init schema
        var options = new DbContextOptionsBuilder<MsSqlMemoryContext>()
                      .UseSqlServer(_connectionString)
                      .Options;
        var factory = new SimpleDbContextFactory<MsSqlMemoryContext>(options);
        _ = new MsSqlMemory(factory, NullLogger<MsSqlMemory>.Instance);

        // Initialize Respawn
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer
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
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    private MsSqlMemory CreateMemory()
    {
        var options = new DbContextOptionsBuilder<MsSqlMemoryContext>()
                      .UseSqlServer(_connectionString)
                      .Options;
        var factory = new SimpleDbContextFactory<MsSqlMemoryContext>(options);
        return new MsSqlMemory(factory, NullLogger<MsSqlMemory>.Instance);
    }

    [Test]
    public async Task GetContextAsync_NoFacts_ReturnsNull()
    {
        var memory = CreateMemory();
        (await memory.GetContextAsync()).ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_StoresFact()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user likes dark mode");

        var result = await memory.GetContextAsync();
        result.ShouldNotBeNull();
        result!.ShouldContain("user likes dark mode");
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
    public async Task AppendHistoryAsync_NoException()
    {
        var memory = CreateMemory();
        await Should.NotThrowAsync(() => memory.AppendHistoryAsync("Test history entry"));
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllPersist()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("alpha");
        await memory.AppendFactAsync("beta");
        await memory.AppendFactAsync("gamma");

        var result = await memory.GetContextAsync();
        result!.ShouldContain("alpha");
        result!.ShouldContain("beta");
        result!.ShouldContain("gamma");
    }

    [Test]
    public async Task SearchAsync_LikeFallback_FindsMatch()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user prefers dark mode");

        var results = await memory.SearchAsync("dark");

        results.ShouldNotBeEmpty();
        results[0].ShouldContain("dark");
    }

    [Test]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user prefers dark mode");

        var results = await memory.SearchAsync("xyzzy");
        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_RespectsNLimit()
    {
        var memory = CreateMemory();
        for (var i = 0; i < 10; i++)
        {
            await memory.AppendFactAsync($"fact number {i} about dogs");
        }

        var results = await memory.SearchAsync("dogs", n: 3);
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
}