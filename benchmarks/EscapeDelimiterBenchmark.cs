using BenchmarkDotNet.Attributes;

namespace PerfBenchmarks;

/// <summary>
/// PromptGuard.EscapeDelimiterContent: fast-path IndexOfAny check vs always triple-Replace.
/// Called on every user message and every tool result.
/// </summary>
[MemoryDiagnoser]
public class EscapeDelimiterBenchmark
{
    private string _noSpecialChars = null!;
    private string _withSpecialChars = null!;
    private string _longMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _noSpecialChars = "Hello, this is a normal user message with no XML special characters. It contains punctuation, numbers 12345, and symbols like @#$%^*()!";
        _withSpecialChars = "The formula is x < 10 && y > 5, use a & b for the <result> tag";
        _longMessage = string.Concat(Enumerable.Repeat("This is a long message without special chars. ", 100));
    }

    // --- Before: always triple Replace ---

    [Benchmark(Baseline = true)]
    public string Before_NoSpecial() => AlwaysReplace(_noSpecialChars);

    [Benchmark]
    public string Before_WithSpecial() => AlwaysReplace(_withSpecialChars);

    [Benchmark]
    public string Before_LongNoSpecial() => AlwaysReplace(_longMessage);

    // --- After: fast-path check ---

    [Benchmark]
    public string After_NoSpecial() => FastPathEscape(_noSpecialChars);

    [Benchmark]
    public string After_WithSpecial() => FastPathEscape(_withSpecialChars);

    [Benchmark]
    public string After_LongNoSpecial() => FastPathEscape(_longMessage);

    private static string AlwaysReplace(string content)
        => content.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string FastPathEscape(string content)
        => content.AsSpan().IndexOfAny("&<>") < 0
            ? content
            : content.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
