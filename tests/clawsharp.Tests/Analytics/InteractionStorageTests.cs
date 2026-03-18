using Clawsharp.Analytics;

namespace Clawsharp.Tests.Analytics;

public sealed class InteractionStorageTests : IDisposable
{
    private readonly string _tempDir;

    public InteractionStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clawsharp-analytics-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private InteractionStorage CreateStorage(string fileName = "interactions.jsonl")
        => new(Path.Combine(_tempDir, fileName));

    private static InteractionRecord MakeRecord(string id = "test-001", string model = "gpt-4o") => new()
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

    [Test]
    public async Task AppendAsync_SingleRecord_ReadAllReturnsIt()
    {
        var storage = CreateStorage();
        var record = MakeRecord();

        await storage.AppendAsync(record);

        var results = await storage.ReadAllAsync();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("test-001");
        results[0].SessionId.ShouldBe("session-a");
        results[0].Channel.ShouldBe("cli");
        results[0].Model.ShouldBe("gpt-4o");
        results[0].UserPrompt.ShouldBe("Hello");
        results[0].Response.ShouldBe("Hi there");
        results[0].InputTokens.ShouldBe(100);
        results[0].OutputTokens.ShouldBe(50);
        results[0].CostUsd.ShouldBe(0.001m);
        results[0].DurationMs.ShouldBe(250);
    }

    [Test]
    public async Task AppendAsync_MultipleRecords_ReadAllReturnsInOrder()
    {
        var storage = CreateStorage("multi.jsonl");

        await storage.AppendAsync(MakeRecord("rec-1", "gpt-4o"));
        await storage.AppendAsync(MakeRecord("rec-2", "claude-3-opus"));
        await storage.AppendAsync(MakeRecord("rec-3", "gemini-pro"));

        var results = await storage.ReadAllAsync();

        results.Count.ShouldBe(3);
        results[0].Id.ShouldBe("rec-1");
        results[0].Model.ShouldBe("gpt-4o");
        results[1].Id.ShouldBe("rec-2");
        results[1].Model.ShouldBe("claude-3-opus");
        results[2].Id.ShouldBe("rec-3");
        results[2].Model.ShouldBe("gemini-pro");
    }

    [Test]
    public async Task CacheInvalidation_AppendAfterRead_ReturnsNewData()
    {
        var storage = CreateStorage("cache.jsonl");

        await storage.AppendAsync(MakeRecord("first"));
        var firstRead = await storage.ReadAllAsync();
        firstRead.Count.ShouldBe(1);

        // Append another record — this should invalidate the cache
        await storage.AppendAsync(MakeRecord("second"));
        var secondRead = await storage.ReadAllAsync();

        secondRead.Count.ShouldBe(2);
        secondRead[0].Id.ShouldBe("first");
        secondRead[1].Id.ShouldBe("second");
    }

    [Test]
    public async Task ReadAllAsync_MissingFile_ReturnsEmptyList()
    {
        var storage = CreateStorage("does-not-exist.jsonl");

        var results = await storage.ReadAllAsync();

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_EmptyFile_ReturnsEmptyList()
    {
        var filePath = Path.Combine(_tempDir, "empty.jsonl");
        await File.WriteAllTextAsync(filePath, "");
        var storage = new InteractionStorage(filePath);

        var results = await storage.ReadAllAsync();

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_MalformedLines_SkipsThemGracefully()
    {
        var filePath = Path.Combine(_tempDir, "malformed.jsonl");

        // Write a valid record, then garbage, then another valid record
        var storage = new InteractionStorage(filePath);
        await storage.AppendAsync(MakeRecord("good-1"));

        // Inject a malformed line directly into the file
        await File.AppendAllTextAsync(filePath, "THIS IS NOT JSON\n");
        await File.AppendAllTextAsync(filePath, "{\"incomplete\": true\n");

        await storage.AppendAsync(MakeRecord("good-2"));

        // Force cache invalidation by creating a fresh storage pointing at the same file
        var freshStorage = new InteractionStorage(filePath);
        var results = await freshStorage.ReadAllAsync();

        results.Count.ShouldBe(2);
        results[0].Id.ShouldBe("good-1");
        results[1].Id.ShouldBe("good-2");
    }

    [Test]
    public async Task ReadAllAsync_WhitespaceOnlyLines_SkipsThem()
    {
        var filePath = Path.Combine(_tempDir, "whitespace.jsonl");
        var storage = new InteractionStorage(filePath);
        await storage.AppendAsync(MakeRecord("only-record"));

        // Inject blank lines
        await File.AppendAllTextAsync(filePath, "\n   \n\n");

        var freshStorage = new InteractionStorage(filePath);
        var results = await freshStorage.ReadAllAsync();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("only-record");
    }

    [Test]
    public async Task ToolCalls_RoundTripsCorrectly()
    {
        var storage = CreateStorage("tools.jsonl");
        var record = new InteractionRecord
        {
            Id = "tool-test",
            SessionId = "session-b",
            Channel = "telegram",
            Model = "claude-3-opus",
            UserPrompt = "list files",
            Response = "Here are the files",
            ToolCalls = [new ToolCallSummary { Name = "list_files", ResultLength = 1024 }],
            ToolIterations = 1,
            InputTokens = 200,
            OutputTokens = 100,
            CacheReadTokens = 50,
            CacheWriteTokens = 10,
            CostUsd = 0.005m,
            CacheSavingsUsd = 0.001m,
            DurationMs = 500,
            Timestamp = DateTimeOffset.UtcNow,
        };

        await storage.AppendAsync(record);
        var results = await storage.ReadAllAsync();

        results.Count.ShouldBe(1);
        results[0].ToolCalls.ShouldNotBeNull();
        results[0].ToolCalls!.Count.ShouldBe(1);
        results[0].ToolCalls![0].Name.ShouldBe("list_files");
        results[0].ToolCalls![0].ResultLength.ShouldBe(1024);
        results[0].ToolIterations.ShouldBe(1);
        results[0].CacheReadTokens.ShouldBe(50);
        results[0].CacheWriteTokens.ShouldBe(10);
        results[0].CacheSavingsUsd.ShouldBe(0.001m);
    }
}
