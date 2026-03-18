using Clawsharp.Cost;

namespace Clawsharp.Tests.Unit.Cost;

[TestFixture]
public sealed class CostStorageEdgeCaseTests : IDisposable
{
    private readonly string _tempDir;

    public CostStorageEdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            $"clawsharp-storage-edge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private CostStorage MakeStorage(string name = "test") =>
        new(Path.Combine(_tempDir, $"{name}.jsonl"));

    private static CostRecord MakeRecord(string id = "r1") => new()
    {
        Id = id,
        SessionId = "session-test",
        Model = "gpt-4o",
        InputTokens = 100,
        OutputTokens = 50,
        CostUsd = 0.001m,
        Timestamp = DateTimeOffset.UtcNow
    };

    // ── Malformed lines ───────────────────────────────────────────────

    [Test]
    public async Task ReadAllAsync_MalformedJsonLines_SkippedSilently()
    {
        var filePath = Path.Combine(_tempDir, "malformed.jsonl");
        var storage = new CostStorage(filePath);

        // Write one valid record first via the API
        await storage.AppendAsync(MakeRecord("valid-1"));

        // Write malformed lines directly to the file (simulating corruption)
        await File.AppendAllTextAsync(filePath, "not json at all\n");
        await File.AppendAllTextAsync(filePath, "{\"broken\": }\n");
        await File.AppendAllTextAsync(filePath, "null\n");

        // Write a second valid record via the API
        await storage.AppendAsync(MakeRecord("valid-2"));

        var records = await storage.ReadAllAsync();

        records.Count.ShouldBe(2, "malformed lines should be silently skipped");
        records.ShouldAllBe(r => r.Id.StartsWith("valid-"));
    }

    [Test]
    public async Task ReadAllAsync_FileWithOnlyMalformedLines_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDir, "all-malformed.jsonl");
        await File.WriteAllTextAsync(filePath, "garbage\n{bad json}\nnull\n");

        var storage = new CostStorage(filePath);
        var records = await storage.ReadAllAsync();

        records.ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_WhitespaceOnlyLines_Skipped()
    {
        var filePath = Path.Combine(_tempDir, "whitespace.jsonl");
        var storage = new CostStorage(filePath);

        await storage.AppendAsync(MakeRecord("r1"));
        // Inject whitespace lines
        await File.AppendAllTextAsync(filePath, "\n   \n\t\n");
        await storage.AppendAsync(MakeRecord("r2"));

        var records = await storage.ReadAllAsync();

        records.Count.ShouldBe(2);
    }

    // ── Cache invalidation ────────────────────────────────────────────

    [Test]
    public async Task ReadAllAsync_AfterAppend_ReturnsFreshData_NotStale()
    {
        var storage = MakeStorage("cache-test");

        // First append + read — populates cache
        await storage.AppendAsync(MakeRecord("r1"));
        var first = await storage.ReadAllAsync();
        first.Count.ShouldBe(1);

        // Append a second record — must invalidate the cache
        await storage.AppendAsync(MakeRecord("r2"));
        var second = await storage.ReadAllAsync();

        second.Count.ShouldBe(2, "cache must be invalidated after AppendAsync");
        second.Select(r => r.Id).ShouldContain("r1");
        second.Select(r => r.Id).ShouldContain("r2");
    }

    [Test]
    public async Task ReadAllAsync_ExternalFileWrite_CacheInvalidatedByMtime()
    {
        var filePath = Path.Combine(_tempDir, "external-write.jsonl");
        var storage = new CostStorage(filePath);

        // Write and read to populate cache
        await storage.AppendAsync(MakeRecord("r1"));
        var cached = await storage.ReadAllAsync();
        cached.Count.ShouldBe(1);

        // Give the FS at least 1ms so mtime differs (some filesystems have 1ms granularity)
        await Task.Delay(10);

        // Directly write a valid JSONL line externally — simulates external modification
        var extraRecord = new CostRecord
        {
            Id = "r2-external",
            SessionId = "external-session",
            Model = "claude-sonnet-4-6",
            InputTokens = 200,
            OutputTokens = 100,
            CostUsd = 0.002m,
            Timestamp = DateTimeOffset.UtcNow
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            extraRecord, CostJsonContext.Default.CostRecord);
        await File.AppendAllTextAsync(filePath, json + "\n");

        // Read again — cache should be invalidated by the mtime change
        var fresh = await storage.ReadAllAsync();

        fresh.Count.ShouldBe(2,
            "external file modification must invalidate the in-memory cache via mtime check");
    }

    // ── Concurrent appends ────────────────────────────────────────────

    [Test]
    public async Task AppendAsync_ConcurrentWriters_AllRecordsPersisted()
    {
        const int writerCount = 5;
        const int recordsPerWriter = 20;
        var storage = MakeStorage("concurrent");

        var tasks = Enumerable.Range(0, writerCount).Select(w => Task.Run(async () =>
        {
            for (var i = 0; i < recordsPerWriter; i++)
            {
                await storage.AppendAsync(MakeRecord($"writer-{w}-record-{i}"));
            }
        }));

        await Task.WhenAll(tasks);

        var records = await storage.ReadAllAsync();

        records.Count.ShouldBe(writerCount * recordsPerWriter,
            "the semaphore must serialize all concurrent writes correctly");

        // All record IDs must be unique and present
        var ids = records.Select(r => r.Id).ToHashSet();
        ids.Count.ShouldBe(writerCount * recordsPerWriter,
            "no records should be lost or duplicated under concurrent write load");
    }

    [Test]
    public async Task AppendAsync_ConcurrentWritersAndReaders_NoExceptions()
    {
        var storage = MakeStorage("concurrent-rw");

        // Seed with one record
        await storage.AppendAsync(MakeRecord("seed"));

        var writeTask = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await storage.AppendAsync(MakeRecord($"write-{i}"));
            }
        });

        var readTask = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                var _ = await storage.ReadAllAsync();
            }
        });

        // Neither task should throw
        Should.NotThrow(() => Task.WhenAll(writeTask, readTask).GetAwaiter().GetResult());
    }

    // ── Multiple sequential appends ───────────────────────────────────

    [Test]
    public async Task AppendAsync_MultipleSequentialAppends_AllPresent()
    {
        var storage = MakeStorage("sequential");

        for (var i = 0; i < 10; i++)
        {
            await storage.AppendAsync(MakeRecord($"seq-{i}"));
        }

        var records = await storage.ReadAllAsync();

        records.Count.ShouldBe(10);
        for (var i = 0; i < 10; i++)
        {
            records.ShouldContain(r => r.Id == $"seq-{i}");
        }
    }
}
