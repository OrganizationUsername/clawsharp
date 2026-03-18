using Clawsharp.Memory.Entities;
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

    [Test]
    public async Task AppendFactAsync_FactPersistedInSqlite()
    {
        await _memory.AppendFactAsync("user prefers dark mode");

        // Query the SQLite DB directly via DbContext
        var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                      .UseSqlite($"Data Source={_dbPath}")
                      .Options;
        await using var ctx = new SqliteMemoryContext(options);
        var facts = await ctx.Facts.ToListAsync();

        facts.Count.ShouldBe(1);
        facts[0].Content.ShouldBe("user prefers dark mode");
        facts[0].CreatedAt.ShouldNotBe(default);
        facts[0].AccessCount.ShouldBe(0);
        facts[0].Id.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task AppendHistoryAsync_HistoryPersistedInSqlite()
    {
        await _memory.AppendHistoryAsync("User discussed CI pipelines");

        var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                      .UseSqlite($"Data Source={_dbPath}")
                      .Options;
        await using var ctx = new SqliteMemoryContext(options);
        var history = await ctx.History.ToListAsync();

        history.Count.ShouldBe(1);
        history[0].Summary.ShouldBe("User discussed CI pipelines");
        history[0].Ts.ShouldNotBe(default);
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllHaveUniqueIds()
    {
        await _memory.AppendFactAsync("fact one");
        await _memory.AppendFactAsync("fact two");
        await _memory.AppendFactAsync("fact three");

        var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                      .UseSqlite($"Data Source={_dbPath}")
                      .Options;
        await using var ctx = new SqliteMemoryContext(options);
        var facts = await ctx.Facts.OrderBy(f => f.Id).ToListAsync();

        facts.Count.ShouldBe(3);
        facts.Select(f => f.Id).Distinct().Count().ShouldBe(3);
        facts[1].Id.ShouldBeGreaterThan(facts[0].Id);
        facts[2].Id.ShouldBeGreaterThan(facts[1].Id);
    }

    [Test]
    public async Task ListFactsAsync_ReturnsFacts_WithCorrectFields()
    {
        await _memory.AppendFactAsync("the sky is blue");
        await _memory.AppendFactAsync("water is wet");

        var facts = await _memory.ListFactsAsync();

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
    public async Task ClearAsync_RemovesAllFacts_PreservesHistory()
    {
        await _memory.AppendFactAsync("fact to delete");
        await _memory.AppendFactAsync("another fact to delete");
        await _memory.AppendHistoryAsync("history entry preserved");

        await _memory.ClearAsync();

        var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                      .UseSqlite($"Data Source={_dbPath}")
                      .Options;
        await using var ctx = new SqliteMemoryContext(options);
        var facts = await ctx.Facts.ToListAsync();
        var history = await ctx.History.ToListAsync();

        facts.ShouldBeEmpty();
        // History is WORM — preserved across clears
        history.Count.ShouldBe(1);
        history[0].Summary.ShouldBe("history entry preserved");
    }

    [Test]
    public async Task SearchAsync_Fts5Search_FindsRelevantFacts()
    {
        await _memory.AppendFactAsync("the quick brown fox jumps over the lazy dog");
        await _memory.AppendFactAsync("cats are independent animals");
        await _memory.AppendFactAsync("the fox is a clever creature");

        var results = await _memory.SearchAsync("fox");

        results.ShouldNotBeEmpty();
        results.ShouldAllBe(r => r.Contains("fox", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task SearchHybridAsync_WithoutEmbedding_FallsBackToLike()
    {
        await _memory.AppendFactAsync("user enjoys hiking in mountains");
        await _memory.AppendFactAsync("user likes reading books");

        var facts = await _memory.SearchHybridAsync("hiking");

        facts.Count.ShouldBe(1);
        facts[0].Content.ShouldContain("hiking");
    }
}