using System.Text.Json;
using Clawsharp.Cost;

namespace Clawsharp.Tests.Unit.Cost;

public sealed class CostRecordBackwardCompatTests
{
    [Test]
    public void Deserialize_OldFormat_IntTokens_DeserializesToLong()
    {
        // Old JSONL format used int for tokens
        const string json = """
            {
                "id": "test-1",
                "session_id": "sess-1",
                "model": "gpt-4o",
                "input_tokens": 1000,
                "output_tokens": 500,
                "cost_usd": 0.0125,
                "timestamp": "2026-03-01T00:00:00Z"
            }
            """;

        var record = JsonSerializer.Deserialize(json, CostJsonContext.Default.CostRecord);
        record.ShouldNotBeNull();
        record.InputTokens.ShouldBe(1000L);
        record.OutputTokens.ShouldBe(500L);
    }

    [Test]
    public void Deserialize_OldFormat_DoubleCost_DeserializesToDecimal()
    {
        // Old JSONL used double for cost
        const string json = """
            {
                "id": "test-2",
                "session_id": "sess-2",
                "model": "gpt-4o",
                "input_tokens": 5000,
                "output_tokens": 2000,
                "cost_usd": 0.055,
                "timestamp": "2026-03-01T00:00:00Z"
            }
            """;

        var record = JsonSerializer.Deserialize(json, CostJsonContext.Default.CostRecord);
        record.ShouldNotBeNull();
        record.CostUsd.ShouldBe(0.055m);
    }

    [Test]
    public void Deserialize_OldFormat_MissingCacheFields_DefaultsToZero()
    {
        // Old format had no cache_read_tokens, cache_write_tokens, cache_savings_usd
        const string json = """
            {
                "id": "test-3",
                "session_id": "sess-3",
                "model": "gpt-4o",
                "input_tokens": 100,
                "output_tokens": 50,
                "cost_usd": 0.001,
                "timestamp": "2026-03-01T00:00:00Z"
            }
            """;

        var record = JsonSerializer.Deserialize(json, CostJsonContext.Default.CostRecord);
        record.ShouldNotBeNull();
        record.CacheReadTokens.ShouldBe(0L);
        record.CacheWriteTokens.ShouldBe(0L);
        record.CacheSavingsUsd.ShouldBe(0m);
    }

    [Test]
    public void Deserialize_NewFormat_AllFieldsPresent()
    {
        const string json = """
            {
                "id": "test-4",
                "session_id": "sess-4",
                "model": "claude-sonnet-4-6",
                "input_tokens": 50000,
                "output_tokens": 10000,
                "cache_read_tokens": 30000,
                "cache_write_tokens": 5000,
                "cache_savings_usd": 0.0045,
                "cost_usd": 0.195,
                "timestamp": "2026-03-12T15:30:00+00:00"
            }
            """;

        var record = JsonSerializer.Deserialize(json, CostJsonContext.Default.CostRecord);
        record.ShouldNotBeNull();
        record.InputTokens.ShouldBe(50000L);
        record.OutputTokens.ShouldBe(10000L);
        record.CacheReadTokens.ShouldBe(30000L);
        record.CacheWriteTokens.ShouldBe(5000L);
        record.CacheSavingsUsd.ShouldBe(0.0045m);
        record.CostUsd.ShouldBe(0.195m);
    }

    [Test]
    public void Deserialize_SubCentValue_PreservePrecision()
    {
        const string json = """
            {
                "id": "test-5",
                "session_id": "sess-5",
                "model": "gpt-4o-mini",
                "input_tokens": 100,
                "output_tokens": 50,
                "cost_usd": 0.000045,
                "timestamp": "2026-03-01T00:00:00Z"
            }
            """;

        var record = JsonSerializer.Deserialize(json, CostJsonContext.Default.CostRecord);
        record.ShouldNotBeNull();
        record.CostUsd.ShouldBe(0.000045m);
    }
}
