using Clawsharp.Cost;

namespace Clawsharp.Tests.Unit.Cost;

public sealed class CostStorageTests : IDisposable
{
    private readonly string _tempDir;

    public CostStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clawsharp-storage-test-{Guid.NewGuid():N}");
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

    [Test]
    public async Task AppendAndReadAll_SingleRecord_RoundTripsCorrectly()
    {
        var filePath = Path.Combine(_tempDir, "roundtrip.jsonl");
        var storage = new CostStorage(filePath);

        var record = new CostRecord
        {
            Id = "test-001",
            SessionId = "session-a",
            Model = "gpt-4o",
            InputTokens = 1000,
            OutputTokens = 500,
            CostUsd = 0.0125m,
            Timestamp = DateTimeOffset.UtcNow
        };

        await storage.AppendAsync(record);

        var records = await storage.ReadAllAsync();

        records.Count.ShouldBe(1);
        records[0].Id.ShouldBe("test-001");
        records[0].SessionId.ShouldBe("session-a");
        records[0].Model.ShouldBe("gpt-4o");
        records[0].InputTokens.ShouldBe(1000);
        records[0].OutputTokens.ShouldBe(500);
        records[0].CostUsd.ShouldBe(0.0125m);
    }

    [Test]
    public async Task ReadAllAsync_EmptyFile_ReturnsEmptyList()
    {
        var filePath = Path.Combine(_tempDir, "empty.jsonl");
        var storage = new CostStorage(filePath);

        var records = await storage.ReadAllAsync();

        records.ShouldBeEmpty();
    }
}
