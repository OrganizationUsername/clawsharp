using Clawsharp.Memory.Entities;

namespace Clawsharp.Memory.Markdown;

public sealed class MarkdownMemory(string dir) : IMemory, IDisposable
{
    private readonly string _historyPath = Path.Combine(dir, "HISTORY.md");

    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly string _memoryPath = Path.Combine(dir, "MEMORY.md");

    public async Task<string?> GetContextAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_memoryPath))
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(_memoryPath, ct);
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AppendFactAsync(string fact, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var line = $"- {fact.ReplaceLineEndings(" ")}\n";
            await File.AppendAllTextAsync(_memoryPath, line, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AppendHistoryAsync(string summary, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var entry = $"\n## {now}\n{summary}\n";
            await File.AppendAllTextAsync(_historyPath, entry, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int n = 5, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var results = new List<string>();
            if (!File.Exists(_memoryPath))
            {
                return results;
            }

            var lines = await File.ReadAllLinesAsync(_memoryPath, ct);

            foreach (var line in lines)
            {
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(line.TrimStart('-', ' '));
                }

                if (results.Count >= n)
                {
                    break;
                }
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<Fact>> SearchHybridAsync(string query, float[]? queryEmbedding = null, int topK = 5,
                                                             CancellationToken ct = default)
    {
        // Markdown backend does not support embeddings — fall back to string contains
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_memoryPath))
            {
                return [];
            }

            var lines = await File.ReadAllLinesAsync(_memoryPath, ct);
            var facts = new List<Fact>();
            long id = 1;
            foreach (var line in lines)
            {
                var content = line.TrimStart('-', ' ');
                if (!string.IsNullOrWhiteSpace(content) && content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new Fact { Id = id, Content = content });
                }

                id++;
                if (facts.Count >= topK)
                {
                    break;
                }
            }

            return facts;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<Fact>> ListFactsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_memoryPath))
            {
                return [];
            }

            var lines = await File.ReadAllLinesAsync(_memoryPath, ct);
            var facts = new List<Fact>();
            long id = 1;
            foreach (var line in lines)
            {
                var content = line.TrimStart('-', ' ');
                if (!string.IsNullOrWhiteSpace(content))
                {
                    facts.Add(new Fact { Id = id++, Content = content });
                }
            }

            return facts;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (File.Exists(_memoryPath))
            {
                File.Delete(_memoryPath);
            }

            // History entries are WORM (write-once read-many) — preserved across clears.
            // They represent immutable compaction snapshots of conversation history.
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Markdown backend has no per-fact timestamps; pruning is a no-op.</summary>
    public Task<int> PruneExpiredFactsAsync(TimeSpan maxAge, CancellationToken ct = default) =>
        Task.FromResult(0);

    public void Dispose() => _lock.Dispose();
}