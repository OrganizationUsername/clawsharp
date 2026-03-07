using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;

namespace PerfBenchmarks;

/// <summary>
/// CostStorage: AsReadOnly() wrapper vs [.. records] spread copy.
/// Called on every cost summary read.
/// </summary>
[MemoryDiagnoser]
public class ListCopyBenchmark
{
    private List<long> _records = null!;

    [Params(100, 1000, 10000)]
    public int RecordCount;

    [GlobalSetup]
    public void Setup()
    {
        _records = Enumerable.Range(0, RecordCount).Select(i => (long)i).ToList();
    }

    [Benchmark(Baseline = true)]
    public IReadOnlyList<long> Before_SpreadCopy()
        => [.. _records];

    [Benchmark]
    public IReadOnlyList<long> After_AsReadOnly()
        => _records.AsReadOnly();
}
