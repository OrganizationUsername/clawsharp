using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;

namespace PerfBenchmarks;

/// <summary>
/// AgentLoop channel dispatch: FrozenDictionary O(1) vs FirstOrDefault O(n).
/// Called on every inbound message.
/// </summary>
[MemoryDiagnoser]
public class ChannelLookupBenchmark
{
    public record ChannelStub(string Name);

    private List<ChannelStub> _channels = null!;
    private FrozenDictionary<string, ChannelStub> _channelMap = null!;
    private string _lookupTarget = null!;

    [Params(4, 10, 18)]
    public int ChannelCount;

    [GlobalSetup]
    public void Setup()
    {
        var names = new[]
        {
            "cli", "telegram", "discord", "slack", "matrix", "email", "irc", "web",
            "signal", "whatsapp", "bluebubbles", "wechat", "mattermost", "nostr",
            "line", "lark", "qq", "wecom"
        };

        _channels = names.Take(ChannelCount).Select(n => new ChannelStub(n)).ToList();
        _channelMap = _channels.ToFrozenDictionary(c => c.Name);
        // Lookup the last channel (worst case for linear scan)
        _lookupTarget = _channels[^1].Name;
    }

    [Benchmark(Baseline = true)]
    public ChannelStub? Before_FirstOrDefault()
        => _channels.FirstOrDefault(c => c.Name == _lookupTarget);

    [Benchmark]
    public ChannelStub? After_FrozenDictionary()
        => _channelMap.TryGetValue(_lookupTarget, out var ch) ? ch : null;
}
