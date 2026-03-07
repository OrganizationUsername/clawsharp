using Clawsharp.Memory.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed class SqliteMemoryTests
{
    private string _dbPath = null!;

    private SqliteMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                      .UseSqlite($"Data Source={_dbPath}")
                      .Options;
        var factory = new SimpleDbContextFactory<SqliteMemoryContext>(options);
        _memory = new SqliteMemory(factory, NullLogger<SqliteMemory>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task GetContextAsync_NoFacts_ReturnsNull()
    {
        var result = await _memory.GetContextAsync();
        result.ShouldBeNull();
    }

    [Test]
    public async Task AppendFactAsync_StoresFact()
    {
        await _memory.AppendFactAsync("user prefers dark mode");

        var result = await _memory.GetContextAsync();
        result.ShouldNotBeNull();
        result!.ShouldContain("user prefers dark mode");
    }

    [Test]
    public async Task GetContextAsync_ReturnsMemoryHeader()
    {
        await _memory.AppendFactAsync("some fact");

        var result = await _memory.GetContextAsync();

        result!.ShouldStartWith("## Memory");
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllReturned()
    {
        await _memory.AppendFactAsync("fact alpha");
        await _memory.AppendFactAsync("fact beta");
        await _memory.AppendFactAsync("fact gamma");

        var result = await _memory.GetContextAsync();

        result.ShouldNotBeNull();
        result!.ShouldContain("fact alpha");
        result!.ShouldContain("fact beta");
        result!.ShouldContain("fact gamma");
    }

    [Test]
    public async Task AppendHistoryAsync_Stores()
    {
        await Should.NotThrowAsync(() => _memory.AppendHistoryAsync("User asked about weather"));
    }

    [Test]
    public async Task SearchAsync_FtsMatch()
    {
        await _memory.AppendFactAsync("user likes pizza");
        await _memory.AppendFactAsync("user dislikes broccoli");

        var results = await _memory.SearchAsync("pizza");

        results.ShouldNotBeEmpty();
        results[0].ShouldContain("pizza");
    }

    [Test]
    public async Task SearchAsync_NoMatch_Empty()
    {
        await _memory.AppendFactAsync("user likes pizza");

        var results = await _memory.SearchAsync("xyzzy");

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_RespectsNLimit()
    {
        for (var i = 0; i < 10; i++)
        {
            await _memory.AppendFactAsync($"fact about cats number {i}");
        }

        var results = await _memory.SearchAsync("cats", n: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Test]
    public async Task GetContextAsync_ReturnsAtMost50Facts()
    {
        for (var i = 0; i < 60; i++)
        {
            await _memory.AppendFactAsync($"fact number {i}");
        }

        var result = await _memory.GetContextAsync();
        var lines = result!.Split('\n').Count(l => l.StartsWith("- "));
        lines.ShouldBe(50);
    }
}