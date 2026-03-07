using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;

namespace PerfBenchmarks;

/// <summary>
/// AuditLogger / WebChannel serialization: SerializeToUtf8Bytes vs Serialize+GetBytes.
/// Called on every audit event and every streaming token.
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmark
{
    private TestEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        _event = new TestEvent
        {
            EventType = "command_execution",
            Timestamp = DateTimeOffset.UtcNow,
            Actor = "telegram:12345",
            Action = "shell:ls -la /home/user/documents",
            Success = true,
            DurationMs = 42
        };
    }

    [Benchmark(Baseline = true)]
    public byte[] Before_SerializeThenGetBytes()
    {
        var json = JsonSerializer.Serialize(_event, TestJsonContext.Default.TestEvent);
        var line = json + "\n";
        return Encoding.UTF8.GetBytes(line);
    }

    [Benchmark]
    public byte[] After_SerializeToUtf8Bytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_event, TestJsonContext.Default.TestEvent);
    }
}

public sealed class TestEvent
{
    public string EventType { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public bool Success { get; set; }
    public long DurationMs { get; set; }
}

[JsonSerializable(typeof(TestEvent))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
