namespace Clawsharp.Providers;

/// <summary>Thread-safe round-robin API key rotator.</summary>
internal sealed class ApiKeyRotator
{
    private readonly string _fallbackKey;
    private readonly IReadOnlyList<string>? _keys;
    private int _index = -1;

    public ApiKeyRotator(string fallbackKey, IReadOnlyList<string>? keys)
    {
        _fallbackKey = fallbackKey;
        _keys = keys is { Count: > 0 } ? keys : null;
    }

    public string GetNext()
    {
        if (_keys is null)
            return _fallbackKey;

        var i = Interlocked.Increment(ref _index);
        return _keys[(i & int.MaxValue) % _keys.Count];
    }
}
