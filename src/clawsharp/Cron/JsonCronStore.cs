using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Cron;

public sealed partial class JsonCronStore : ICronStore
{
    private readonly string _filePath;

    private readonly ILogger _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonCronStore(string baseDir, ILogger? logger = null)
    {
        _filePath = Path.Combine(baseDir, "cron", "jobs.json");
        _logger = logger ?? NullLogger.Instance;
    }

    public Task InitAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CronJob>> LoadAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await ReadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(CronJob job, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var jobs = await ReadAsync(ct);
            var idx = jobs.FindIndex(j => j.Id == job.Id);
            if (idx >= 0)
            {
                jobs[idx] = job;
            }
            else
            {
                jobs.Add(job);
            }

            await WriteAsync(jobs, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var jobs = await ReadAsync(ct);
            jobs.RemoveAll(j => j.Id == id);
            await WriteAsync(jobs, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateRunStatsAsync(string id, string lastRunAt, int runCount, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var jobs = await ReadAsync(ct);
            var job = jobs.Find(j => j.Id == id);
            if (job is not null)
            {
                job.LastRunAt = lastRunAt;
                job.RunCount = runCount;
                await WriteAsync(jobs, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<CronJob>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            return JsonSerializer.Deserialize(json, CronJsonContext.WithConverters.ListCronJob) ?? [];
        }
        catch (Exception ex)
        {
            LogCronStoreReadFailed(_logger, ex, _filePath);
            return [];
        }
    }

    private async Task WriteAsync(List<CronJob> jobs, CancellationToken ct)
    {
        try
        {
            var tmp = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(jobs, CronJsonContext.WithConverters.ListCronJob);
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, _filePath, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCronStoreWriteFailed(_logger, ex, _filePath);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to read cron store from {Path}")]
    private static partial void LogCronStoreReadFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to write cron store to {Path}")]
    private static partial void LogCronStoreWriteFailed(ILogger logger, Exception exception, string path);
}