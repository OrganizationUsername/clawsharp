using Clawsharp.Memory.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Clawsharp.Tests.Integration.Memory;

[TestFixture]
[Category("Integration")]
public sealed class RedisMemoryTests
{
    private RedisContainer _container = null!;

    private IConnectionMultiplexer _redis = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new RedisBuilder("redis/redis-stack:latest").Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync($"{_container.GetConnectionString()},allowAdmin=true");

        // Init schema — must call a method to trigger lazy index creation
        var memory = CreateMemory();
        await memory.AppendFactAsync("schema init");
        await memory.ClearAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _redis.Dispose();
        await _container.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        // Flush all data between tests
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();
    }

    private RedisMemory CreateMemory()
    {
        return new RedisMemory(
            _redis,
            NullLogger<RedisMemory>.Instance);
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

        // History is not part of GetContextAsync — just verify no exception
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
    public async Task SearchAsync_FindsMatch()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user likes pizza");
        await memory.AppendFactAsync("user dislikes broccoli");

        // Give RediSearch a moment to index
        await Task.Delay(100);

        var results = await memory.SearchAsync("pizza");

        results.ShouldNotBeEmpty();
        results[0].ShouldContain("pizza");
    }

    [Test]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user likes pizza");

        await Task.Delay(100);

        var results = await memory.SearchAsync("xyzzynotarealword");

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

        await Task.Delay(200);

        var results = await memory.SearchAsync("cats", n: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Test]
    public async Task AppendFactAsync_MultipleFacts_AllHaveUniqueIds()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("fact one");
        await memory.AppendFactAsync("fact two");
        await memory.AppendFactAsync("fact three");

        var facts = await memory.ListFactsAsync();

        facts.Count.ShouldBe(3);
        facts.Select(f => f.Id).Distinct().Count().ShouldBe(3);
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
    public async Task ListFactsAsync_OrderedNewestFirst()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("first fact");
        await memory.AppendFactAsync("second fact");
        await memory.AppendFactAsync("third fact");

        var facts = await memory.ListFactsAsync();

        facts.Count.ShouldBe(3);
        // Newest first (highest ID first)
        facts[0].Id.ShouldBeGreaterThan(facts[1].Id);
        facts[1].Id.ShouldBeGreaterThan(facts[2].Id);
    }

    [Test]
    public async Task ClearAsync_RemovesAllFacts_PreservesHistory()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("fact to delete");
        await memory.AppendFactAsync("another fact to delete");
        await memory.AppendHistoryAsync("history entry preserved");

        await memory.ClearAsync();

        var facts = await memory.ListFactsAsync();
        facts.ShouldBeEmpty();

        // History should still exist in Redis
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var historyKeys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: "clawsharp:history:*"))
        {
            var keyStr = key.ToString();
            if (keyStr != "clawsharp:history:seq")
            {
                historyKeys.Add(keyStr);
            }
        }

        var summaries = new List<string>();
        foreach (var hk in historyKeys)
        {
            var summary = db.HashGet(hk, "summary").ToString();
            summaries.Add(summary);
        }

        summaries.ShouldContain("history entry preserved");
    }

    [Test]
    public async Task AppendHistoryAsync_MultipleEntries_AllPreservedAcrossClear()
    {
        var memory = CreateMemory();
        await memory.AppendHistoryAsync("first summary");
        await memory.AppendHistoryAsync("second summary");

        // Clear should not affect history (WORM semantics)
        await memory.ClearAsync();

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var summaries = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: "clawsharp:history:*"))
        {
            var keyStr = key.ToString();
            if (keyStr == "clawsharp:history:seq")
            {
                continue;
            }

            var summary = await db.HashGetAsync(key, "summary");
            if (!summary.IsNullOrEmpty)
            {
                summaries.Add(summary.ToString());
            }
        }

        summaries.ShouldContain("first summary");
        summaries.ShouldContain("second summary");
    }

    [Test]
    public async Task AppendFactAsync_FactPersistedInRedis()
    {
        var memory = CreateMemory();

        await memory.AppendFactAsync("user prefers vim keybindings");

        var db = _redis.GetDatabase();
        // Fact should be at key clawsharp:fact:1 (first fact after flush)
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var factKeys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: "clawsharp:fact:*"))
        {
            var keyStr = key.ToString();
            if (keyStr != "clawsharp:fact:seq")
            {
                factKeys.Add(keyStr);
            }
        }

        factKeys.Count.ShouldBe(1);
        var content = await db.HashGetAsync(factKeys[0], "content");
        content.ToString().ShouldBe("user prefers vim keybindings");

        var createdAt = await db.HashGetAsync(factKeys[0], "createdAt");
        createdAt.IsNullOrEmpty.ShouldBeFalse();

        var accessCount = await db.HashGetAsync(factKeys[0], "accessCount");
        ((int)accessCount).ShouldBe(0);
    }

    [Test]
    public async Task AppendHistoryAsync_HistoryPersistedInRedis()
    {
        var memory = CreateMemory();

        await memory.AppendHistoryAsync("User discussed deployment strategies");

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var historyKeys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: "clawsharp:history:*"))
        {
            var keyStr = key.ToString();
            if (keyStr != "clawsharp:history:seq")
            {
                historyKeys.Add(keyStr);
            }
        }

        historyKeys.Count.ShouldBe(1);
        var summary = await db.HashGetAsync(historyKeys[0], "summary");
        summary.ToString().ShouldBe("User discussed deployment strategies");

        var ts = await db.HashGetAsync(historyKeys[0], "ts");
        ts.IsNullOrEmpty.ShouldBeFalse();
    }

    [Test]
    public async Task SearchHybridAsync_NoEmbedding_FallsBackToText()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("user likes chocolate");
        await memory.AppendFactAsync("user hates vanilla");

        await Task.Delay(100);

        var results = await memory.SearchHybridAsync("chocolate");

        results.ShouldNotBeEmpty();
        results[0].Content.ShouldContain("chocolate");
    }

    [Test]
    public async Task SearchHybridAsync_EmptyEmbedding_FallsBackToText()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("cats are great pets");

        await Task.Delay(100);

        var results = await memory.SearchHybridAsync("cats", queryEmbedding: []);

        results.ShouldNotBeEmpty();
        results[0].Content.ShouldContain("cats");
    }

    [Test]
    public async Task PruneExpiredFactsAsync_DeletesOldFacts()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("old fact");

        // Manually backdate the fact's createdAt
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        await foreach (var key in server.KeysAsync(pattern: "clawsharp:fact:*"))
        {
            if (key.ToString() == "clawsharp:fact:seq")
            {
                continue;
            }

            await db.HashSetAsync(key,
            [
                new HashEntry("createdAt", DateTimeOffset.UtcNow.AddDays(-10).ToString("O")),
            ]);
        }

        await memory.AppendFactAsync("new fact");

        // Prune facts older than 5 days
        var pruned = await memory.PruneExpiredFactsAsync(TimeSpan.FromDays(5));

        pruned.ShouldBe(1);

        var remaining = await memory.ListFactsAsync();
        remaining.Count.ShouldBe(1);
        remaining[0].Content.ShouldBe("new fact");
    }

    [Test]
    public async Task PruneExpiredFactsAsync_NoExpired_ReturnsZero()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("recent fact");

        var pruned = await memory.PruneExpiredFactsAsync(TimeSpan.FromDays(30));

        pruned.ShouldBe(0);
    }

    [Test]
    public async Task SearchAsync_AccessCountIncrementedOnHybridSearch()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("test access tracking");

        await Task.Delay(100);

        // Search via hybrid (which updates access counts)
        await memory.SearchHybridAsync("access tracking");

        // Give time for access count update
        await Task.Delay(50);

        var facts = await memory.ListFactsAsync();
        facts.Count.ShouldBe(1);
        facts[0].AccessCount.ShouldBeGreaterThanOrEqualTo(1);
        facts[0].LastAccessedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task RediSearchIndex_CreatedOnInit()
    {
        var memory = CreateMemory();
        // Trigger initialization
        await memory.AppendFactAsync("trigger init");

        var db = _redis.GetDatabase();
        var ft = db.FT();

        // Index should exist
        var info = await ft.InfoAsync("clawsharp:idx:facts");
        info.ShouldNotBeNull();
    }

    [Test]
    public async Task ClearAsync_FactSequenceReset()
    {
        var memory = CreateMemory();
        await memory.AppendFactAsync("fact 1");
        await memory.AppendFactAsync("fact 2");

        await memory.ClearAsync();

        var db = _redis.GetDatabase();
        var seqExists = await db.KeyExistsAsync("clawsharp:fact:seq");
        seqExists.ShouldBeFalse();
    }

    [Test]
    public async Task GetContextAsync_ReturnsAtMost50Facts()
    {
        var memory = CreateMemory();
        for (var i = 0; i < 60; i++)
        {
            await memory.AppendFactAsync($"fact number {i}");
        }

        await Task.Delay(200);

        var result = await memory.GetContextAsync();
        result.ShouldNotBeNull();
        var lines = result!.Split('\n').Count(l => l.StartsWith("- "));
        lines.ShouldBeLessThanOrEqualTo(50);
    }
}
