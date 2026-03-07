using Clawsharp.Memory.Markdown;
using Clawsharp.Memory.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed class MemoryPruningTests
{
    private string _dbPath = null!;

    private SqliteMemory _memory = null!;

    private SimpleDbContextFactory<SqliteMemoryContext> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                      .UseSqlite($"Data Source={_dbPath}")
                      .Options;
        _factory = new SimpleDbContextFactory<SqliteMemoryContext>(options);
        _memory = new SqliteMemory(_factory, NullLogger<SqliteMemory>.Instance);
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
    public async Task PruneExpiredFacts_NoOldFacts_ReturnsZero()
    {
        // Insert a fresh fact (created now)
        await _memory.AppendFactAsync("fresh fact");

        var pruned = await _memory.PruneExpiredFactsAsync(TimeSpan.FromDays(1));

        pruned.ShouldBe(0);
    }

    [Test]
    public async Task PruneExpiredFacts_OldFacts_RemovesThem()
    {
        await _memory.AppendFactAsync("old fact");

        // Backdate the fact to 10 days ago via raw SQL
        await using var context = _factory.CreateDbContext();
        var oldDate = DateTimeOffset.UtcNow.AddDays(-10).ToString("O");
        await context.Database.ExecuteSqlAsync(
            $"UPDATE facts SET \"CreatedAt\" = {oldDate}");

        var pruned = await _memory.PruneExpiredFactsAsync(TimeSpan.FromDays(1));

        pruned.ShouldBe(1);

        // Verify the fact is gone
        var remaining = await _memory.ListFactsAsync();
        remaining.ShouldBeEmpty();
    }

    [Test]
    public async Task PruneExpiredFacts_EmptyDatabase_ReturnsZero()
    {
        var pruned = await _memory.PruneExpiredFactsAsync(TimeSpan.FromDays(1));
        pruned.ShouldBe(0);
    }

    [Test]
    public async Task PruneExpiredFacts_AllOld_RemovesAll()
    {
        await _memory.AppendFactAsync("fact one");
        await _memory.AppendFactAsync("fact two");
        await _memory.AppendFactAsync("fact three");

        // Backdate all facts to 60 days ago
        await using var context = _factory.CreateDbContext();
        var oldDate = DateTimeOffset.UtcNow.AddDays(-60).ToString("O");
        await context.Database.ExecuteSqlAsync(
            $"UPDATE facts SET \"CreatedAt\" = {oldDate}");

        var pruned = await _memory.PruneExpiredFactsAsync(TimeSpan.FromDays(30));

        pruned.ShouldBe(3);

        var remaining = await _memory.ListFactsAsync();
        remaining.ShouldBeEmpty();
    }

    [Test]
    public async Task MarkdownMemory_PruneExpiredFacts_AlwaysReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var mdMemory = new MarkdownMemory(dir);
            await mdMemory.AppendFactAsync("some fact");

            var pruned = await mdMemory.PruneExpiredFactsAsync(TimeSpan.FromDays(1));

            pruned.ShouldBe(0);

            // Verify facts are still present
            var facts = await mdMemory.ListFactsAsync();
            facts.Count.ShouldBe(1);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}