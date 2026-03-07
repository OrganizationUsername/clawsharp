using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;

namespace PerfBenchmarks;

/// <summary>
/// ShellGuard: ToLowerInvariant + case-sensitive regex vs IgnoreCase regex directly.
/// Called on every shell tool invocation.
/// </summary>
[MemoryDiagnoser]
public class ShellGuardBenchmark
{
    private string _safeCommand = null!;
    private string _longCommand = null!;

    // Case-sensitive patterns (old approach: match against lowered input)
    private static readonly Regex[] OldPatterns =
    [
        new(@"\brm\s+(-[a-z]*[rf][a-z]*\b|--recursive\b|--force\b)", RegexOptions.Compiled),
        new(@"\bsudo\b", RegexOptions.Compiled),
        new(@"\beval\b", RegexOptions.Compiled),
        new(@"\bgit\s+push\b", RegexOptions.Compiled),
        new(@"\|\s*bash\b", RegexOptions.Compiled),
    ];

    // IgnoreCase patterns (new approach: match against original input)
    private static readonly Regex[] NewPatterns =
    [
        new(@"\brm\s+(-[a-z]*[rf][a-z]*\b|--recursive\b|--force\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bsudo\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\beval\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bgit\s+push\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\|\s*bash\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    [GlobalSetup]
    public void Setup()
    {
        _safeCommand = "dotnet test clawsharp.Tests/clawsharp.Tests.csproj --filter \"FullyQualifiedName!~Integration\" --verbosity quiet";
        _longCommand = "find /home/user/projects -name '*.cs' -type f -exec grep -l 'IChannel' {} + | sort | head -20";
    }

    [Benchmark(Baseline = true)]
    public bool Before_LowerThenMatch_Safe()
    {
        var lower = _safeCommand.ToLowerInvariant();
        foreach (var p in OldPatterns)
            if (p.IsMatch(lower)) return true;
        return false;
    }

    [Benchmark]
    public bool After_IgnoreCase_Safe()
    {
        foreach (var p in NewPatterns)
            if (p.IsMatch(_safeCommand)) return true;
        return false;
    }

    [Benchmark]
    public bool Before_LowerThenMatch_Long()
    {
        var lower = _longCommand.ToLowerInvariant();
        foreach (var p in OldPatterns)
            if (p.IsMatch(lower)) return true;
        return false;
    }

    [Benchmark]
    public bool After_IgnoreCase_Long()
    {
        foreach (var p in NewPatterns)
            if (p.IsMatch(_longCommand)) return true;
        return false;
    }
}
