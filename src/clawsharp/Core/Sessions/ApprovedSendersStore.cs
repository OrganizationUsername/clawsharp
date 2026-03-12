using System.Text.Json.Serialization;
using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Clawsharp.Config.Channels;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Sessions;

/// <summary>
///     Persistent store of dynamically approved senders per channel.
///     Consulted alongside static <see cref="ChannelConfig.AllowFrom" /> lists so that
///     pairing approvals take effect without editing the config file.
///     Uses an in-memory cache to avoid disk I/O on the hot path (every incoming message).
///     Thread-safe via <see cref="JsonFileStore{T}"/> for persistence and a local lock for the cache.
/// </summary>
public sealed class ApprovedSendersStore : IDisposable
{
    private static readonly string StorePath = Path.Combine(
        ConfigLoader.ExpandHome("~/.clawsharp"), "approved-senders.json");

    private readonly JsonFileStore<Dictionary<string, List<string>>> _store;

    private readonly ILogger<ApprovedSendersStore> _logger;

    /// <summary>In-memory cache of approved senders, loaded once from disk and refreshed on writes.</summary>
    private Dictionary<string, HashSet<string>>? _cache;

    private readonly Lock _cacheLock = new();

    public ApprovedSendersStore(ILogger<ApprovedSendersStore> logger)
    {
        _logger = logger;
        _store = new JsonFileStore<Dictionary<string, List<string>>>(
            StorePath, ApprovedSendersJsonContext.Default.DictionaryStringListString);
    }

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();

    /// <summary>Returns <c>true</c> if the sender is in the dynamic approved store for the given channel.</summary>
    public async ValueTask<bool> IsApprovedAsync(string channel, string senderId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheAsync(ct).ConfigureAwait(false);
        lock (_cacheLock)
        {
            return cache.TryGetValue(channel, out var senders) &&
                   senders.Contains(senderId);
        }
    }

    /// <summary>Adds a sender to the approved store for the given channel.</summary>
    public async Task AddAsync(string channel, string senderId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheAsync(ct).ConfigureAwait(false);

        bool needSave;
        lock (_cacheLock)
        {
            if (!cache.TryGetValue(channel, out var senders))
            {
                senders = new HashSet<string>(StringComparer.Ordinal);
                cache[channel] = senders;
            }

            needSave = senders.Add(senderId);
        }

        if (needSave)
        {
            await SaveCacheAsync(cache, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Ensures the in-memory cache is loaded from disk.</summary>
    private async Task<Dictionary<string, HashSet<string>>> EnsureCacheAsync(CancellationToken ct)
    {
        lock (_cacheLock)
        {
            if (_cache is not null)
            {
                return _cache;
            }
        }

        // Load from disk (this acquires the store's internal lock)
        var raw = await _store.LoadAsync(ct).ConfigureAwait(false);
        var result = ToHashSetDict(raw);

        lock (_cacheLock)
        {
            // Double-check after async gap
            _cache ??= result;
            return _cache;
        }
    }

    private async Task SaveCacheAsync(Dictionary<string, HashSet<string>> cache, CancellationToken ct)
    {
        Dictionary<string, List<string>> serializable;
        lock (_cacheLock)
        {
            serializable = new Dictionary<string, List<string>>(cache.Count, StringComparer.Ordinal);
            foreach (var (channel, senders) in cache)
            {
                serializable[channel] = [.. senders];
            }
        }

        await _store.SaveAsync(serializable, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, HashSet<string>> ToHashSetDict(Dictionary<string, List<string>> raw)
    {
        var result = new Dictionary<string, HashSet<string>>(raw.Count, StringComparer.Ordinal);
        foreach (var (channel, senders) in raw)
        {
            result[channel] = new HashSet<string>(senders, StringComparer.Ordinal);
        }

        return result;
    }
}

[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
internal partial class ApprovedSendersJsonContext : JsonSerializerContext;