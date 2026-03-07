using System.Text.Json;
using Clawsharp.Config;

namespace Clawsharp.Cost;

/// <summary>
/// Append-only JSONL storage for cost records.
/// Persists to ~/.clawsharp/costs.jsonl.
/// Includes a simple in-memory cache that is invalidated on writes.
/// </summary>
public sealed class CostStorage
{
    private readonly string _filePath;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Simple cache: stores parsed records and the last-modified timestamp of the file
    // when the cache was populated. Invalidated on every AppendAsync call.
    private List<CostRecord>? _cachedRecords;

    private DateTimeOffset _cacheTimestamp;

    private readonly object _cacheLock = new();

    public CostStorage()
    {
        var dir = ConfigLoader.ExpandHome("~/.clawsharp");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "costs.jsonl");
    }

    /// <summary>Test-only constructor with custom file path.</summary>
    internal CostStorage(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        _filePath = filePath;
    }

    /// <summary>Append a single cost record as a JSONL line.</summary>
    public async Task AppendAsync(CostRecord record, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(record, CostJsonContext.Default.CostRecord);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_filePath, json + "\n", ct).ConfigureAwait(false);

            // Invalidate the cache — next ReadAllAsync will re-read the file
            lock (_cacheLock)
            {
                _cachedRecords = null;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Read all cost records from the JSONL file.
    /// Uses a simple in-memory cache that is invalidated when a new record is written
    /// or when the file's last-write time changes (e.g., external edits).
    /// </summary>
    public async Task<IReadOnlyList<CostRecord>> ReadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        // Check if cached data is still valid
        var lastWrite = File.GetLastWriteTimeUtc(_filePath);
        lock (_cacheLock)
        {
            if (_cachedRecords is not null && lastWrite <= _cacheTimestamp.UtcDateTime)
            {
                return _cachedRecords.AsReadOnly();
            }
        }

        // Cache miss — re-read from disk
        var records = new List<CostRecord>();
        await foreach (var line in File.ReadLinesAsync(_filePath, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize(line, CostJsonContext.Default.CostRecord);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines — best-effort
            }
        }

        // Update cache
        lock (_cacheLock)
        {
            _cachedRecords = records;
            _cacheTimestamp = new DateTimeOffset(lastWrite, TimeSpan.Zero);
        }

        return records.AsReadOnly();
    }
}