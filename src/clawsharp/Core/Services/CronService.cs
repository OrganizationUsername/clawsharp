using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clawsharp.Config;
using Clawsharp.Cron;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Services;

/// <summary>
///     Dynamic cron service. Loads jobs from an ICronStore on startup, seeds static
///     config entries, then polls every 10 seconds while enabled jobs exist.
///     The internal timer self-suspends when no enabled jobs remain and wakes up
///     automatically when AddJobAsync / UpdateJobAsync adds an enabled job.
/// </summary>
public sealed partial class CronService : LifecycleBackgroundService
{
    private const int PollIntervalMs = 10_000;

    private readonly IMessageBus _bus;

    private readonly AppConfig _config;

    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Dictionary<string, CronJob> _jobs = new();

    private readonly SemaphoreSlim _jobsLock = new(1, 1);

    private readonly ILogger<CronService> _logger;

    private readonly ICronStore _store;

    // 0→1 semaphore used as a wake signal: Release() to wake, WaitAsync() to sleep
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    public CronService(ICronStore store, IMessageBus bus, IOptions<AppConfig> configOptions, ILogger<CronService> logger)
    {
        _store = store;
        _bus = bus;
        _config = configOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _store.InitAsync(stoppingToken);

            var loaded = await _store.LoadAllAsync(stoppingToken);

            await _jobsLock.WaitAsync(stoppingToken);
            try
            {
                foreach (var job in loaded)
                {
                    _jobs[job.Id] = job;
                }

                // Seed static config entries with stable deterministic IDs
                foreach (var entry in _config.Cron)
                {
                    if (string.IsNullOrWhiteSpace(entry.Schedule) || string.IsNullOrWhiteSpace(entry.Message))
                    {
                        continue;
                    }

                    var id = MakeConfigId(entry);
                    if (!_jobs.ContainsKey(id))
                    {
                        var job = new CronJob
                        {
                            Id = id,
                            Name = entry.Message.Length > 40
                                ? entry.Message[..40] + "…"
                                : entry.Message,
                            ScheduleKind = CronScheduleKind.Cron,
                            ScheduleExpr = entry.Schedule,
                            Channel = entry.Channel,
                            Message = entry.Message,
                            SenderId = entry.SenderId,
                            Source = CronSource.Config,
                            Model = entry.Model,
                            Provider = entry.Provider
                        };
                        _jobs[id] = job;
                        await _store.UpsertAsync(job, stoppingToken);
                    }
                }

                LogJobsLoaded(_logger, _jobs.Count);
            }
            finally
            {
                _jobsLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogInitializationFailed(_logger, ex);
        }
        finally
        {
            _initialized.TrySetResult();
        }

        // Wake up immediately if we already have enabled jobs
        bool hasEnabledOnStart;
        await _jobsLock.WaitAsync(stoppingToken);
        try
        {
            hasEnabledOnStart = _jobs.Values.Any(j => j.Enabled);
        }
        finally
        {
            _jobsLock.Release();
        }

        if (hasEnabledOnStart)
        {
            SignalWakeUp();
        }

        // Outer loop: sleep until enabled jobs appear
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _wakeSignal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            LogTimerActive(_logger);

            // Inner loop: poll every 10 seconds while enabled jobs exist
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Reload from the backing store so that jobs added externally
                    // (e.g. via CLI `clawsharp cron add`) are picked up without a restart.
                    await ReloadFromStoreAsync(stoppingToken);

                    await FireDueJobsAsync(stoppingToken);

                    bool hasEnabled;
                    await _jobsLock.WaitAsync(stoppingToken);
                    try
                    {
                        hasEnabled = _jobs.Values.Any(j => j.Enabled);
                    }
                    finally
                    {
                        _jobsLock.Release();
                    }

                    if (!hasEnabled)
                    {
                        LogSuspendingTimer(_logger);
                        break;
                    }

                    await Task.Delay(PollIntervalMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    // ── Public API for CronTool ───────────────────────────────────────────────

    public async Task<CronJob> AddJobAsync(CronJob job, CancellationToken ct = default)
    {
        await _initialized.Task.WaitAsync(ct);
        await _jobsLock.WaitAsync(ct);
        try
        {
            _jobs[job.Id] = job;
        }
        finally
        {
            _jobsLock.Release();
        }

        await _store.UpsertAsync(job, ct);

        if (job.Enabled)
        {
            SignalWakeUp();
        }

        LogJobAdded(_logger, job.Id, job.ScheduleKind.Value, job.ScheduleExpr);
        return job;
    }

    public async Task<IReadOnlyList<CronJob>> ListJobsAsync(CancellationToken ct = default)
    {
        await _initialized.Task.WaitAsync(ct);
        await _jobsLock.WaitAsync(ct);
        try
        {
            return _jobs.Values.ToList();
        }
        finally
        {
            _jobsLock.Release();
        }
    }

    public async Task<bool> RemoveJobAsync(string id, CancellationToken ct = default)
    {
        await _initialized.Task.WaitAsync(ct);
        bool removed;
        await _jobsLock.WaitAsync(ct);
        try
        {
            removed = _jobs.Remove(id);
        }
        finally
        {
            _jobsLock.Release();
        }

        if (removed)
        {
            await _store.DeleteAsync(id, ct);
            LogJobRemoved(_logger, id);
        }

        return removed;
    }

    public async Task<CronJob?> UpdateJobAsync(CronJob job, CancellationToken ct = default)
    {
        await _initialized.Task.WaitAsync(ct);
        await _jobsLock.WaitAsync(ct);
        try
        {
            if (!_jobs.ContainsKey(job.Id))
            {
                return null;
            }

            _jobs[job.Id] = job;
        }
        finally
        {
            _jobsLock.Release();
        }

        await _store.UpsertAsync(job, ct);

        if (job.Enabled)
        {
            SignalWakeUp();
        }

        return job;
    }

    public async Task<string> RunJobNowAsync(string id, CancellationToken ct = default)
    {
        await _initialized.Task.WaitAsync(ct);
        CronJob? job;
        await _jobsLock.WaitAsync(ct);
        try
        {
            _jobs.TryGetValue(id, out job);
        }
        finally
        {
            _jobsLock.Release();
        }

        if (job is null)
        {
            return $"No job with id '{id}'.";
        }

        await FireJobAsync(job, ct);
        return $"Fired job '{job.Id}' ({job.Name ?? job.ScheduleExpr}).";
    }

    // ── Store reload ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Merges the backing store into the in-memory cache so that jobs added
    ///     externally (CLI commands, other processes) are picked up on the next tick.
    ///     Existing in-memory entries are updated; new store entries are added; jobs
    ///     deleted from the store are removed from memory (unless they were added
    ///     in-process during this session via <see cref="AddJobAsync"/>).
    /// </summary>
    private async Task ReloadFromStoreAsync(CancellationToken ct)
    {
        IReadOnlyList<CronJob> stored;
        try
        {
            stored = await _store.LoadAllAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogStoreReloadFailed(_logger, ex);
            return; // proceed with stale in-memory data
        }

        await _jobsLock.WaitAsync(ct);
        try
        {
            var storeIds = new HashSet<string>(stored.Count, StringComparer.Ordinal);
            foreach (var storeJob in stored)
            {
                storeIds.Add(storeJob.Id);

                if (_jobs.TryGetValue(storeJob.Id, out var existing))
                {
                    // Preserve in-memory runtime state (LastRunAt, RunCount) when
                    // it is more recent than what is on disk, because the in-process
                    // FireJobAsync updates memory first and persists best-effort.
                    if (existing.LastRunAt is not null &&
                        (storeJob.LastRunAt is null ||
                         string.Compare(existing.LastRunAt, storeJob.LastRunAt, StringComparison.Ordinal) > 0))
                    {
                        storeJob.LastRunAt = existing.LastRunAt;
                        storeJob.RunCount = existing.RunCount;
                    }
                }

                _jobs[storeJob.Id] = storeJob;
            }

            // Remove jobs that were deleted from the store externally.
            var toRemove = _jobs.Keys.Where(id => !storeIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                _jobs.Remove(id);
            }
        }
        finally
        {
            _jobsLock.Release();
        }
    }

    // ── Firing logic ──────────────────────────────────────────────────────────

    private async Task FireDueJobsAsync(CancellationToken ct)
    {
        List<CronJob> snapshot;
        await _jobsLock.WaitAsync(ct);
        try
        {
            snapshot = _jobs.Values.Where(j => j.Enabled).ToList();
        }
        finally
        {
            _jobsLock.Release();
        }

        var utcNow = DateTimeOffset.UtcNow;
        foreach (var job in snapshot)
        {
            var jobNow = ResolveJobLocalTime(job, utcNow);
            if (!ShouldFire(job, jobNow))
            {
                continue;
            }

            try
            {
                await FireJobAsync(job, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                LogJobFireError(_logger, ex, job.Id);
            }
        }
    }

    private async Task FireJobAsync(CronJob job, CancellationToken ct)
    {
        var preview = job.Message.Length > 80 ? job.Message[..80] + "…" : job.Message;
        LogJobFiring(_logger, job.ScheduleKind.Value, job.ScheduleExpr, job.Channel, preview);

        if (!ChannelName.TryFromValue(job.Channel, out var channelName))
        {
            LogInvalidChannel(_logger, job.Channel, job.Id);
            return;
        }

        await _bus.PublishAsync(new InboundMessage(
            Channel: channelName,
            SenderId: job.SenderId,
            SenderName: "cron",
            Text: job.Message,
            ArrivedAt: DateTimeOffset.UtcNow,
            ModelOverride: job.Model,
            ProviderOverride: job.Provider
        ), ct);

        var nowStr = DateTimeOffset.UtcNow.ToString("O");
        var newCount = job.RunCount + 1;

        // Update in-memory state under lock
        await _jobsLock.WaitAsync(ct);
        try
        {
            if (_jobs.TryGetValue(job.Id, out var current))
            {
                current.LastRunAt = nowStr;
                current.RunCount = newCount;
                if (current.ScheduleKind == CronScheduleKind.At)
                {
                    current.Enabled = false; // one-shot: auto-disable
                }
            }
        }
        finally
        {
            _jobsLock.Release();
        }

        // Persist stats (best-effort, 5s timeout to avoid blocking shutdown indefinitely)
        using var statsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _store.UpdateRunStatsAsync(job.Id, nowStr, newCount, statsCts.Token);
        }
        catch (Exception ex)
        {
            LogPersistStatsFailed(_logger, ex, job.Id);
        }

        // For "at" jobs, also persist the disabled state
        if (job.ScheduleKind == CronScheduleKind.At)
        {
            try
            {
                using var atCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _jobsLock.WaitAsync(atCts.Token);
                CronJob? disabled;
                try
                {
                    _jobs.TryGetValue(job.Id, out disabled);
                }
                finally
                {
                    _jobsLock.Release();
                }

                if (disabled is not null)
                {
                    await _store.UpsertAsync(disabled, atCts.Token);
                }
            }
            catch (Exception ex)
            {
                LogPersistJobFailed(_logger, ex, job.Id);
            }
        }
    }

    private void SignalWakeUp()
    {
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        } // already signaled — fine
    }

    // ── Timezone resolution ────────────────────────────────────────────────────

    private DateTimeOffset ResolveJobLocalTime(CronJob job, DateTimeOffset utcNow)
    {
        if (string.IsNullOrEmpty(job.Tz))
        {
            return utcNow;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(job.Tz);
            return TimeZoneInfo.ConvertTime(utcNow, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            LogInvalidTimezone(_logger, job.Id, job.Tz);
            return utcNow;
        }
    }

    // ── Schedule matching ─────────────────────────────────────────────────────

    private static bool ShouldFire(CronJob job, DateTimeOffset now)
    {
        if (job.ScheduleKind == CronScheduleKind.Cron)
        {
            return ShouldFireCron(job, now);
        }

        if (job.ScheduleKind == CronScheduleKind.Every)
        {
            return ShouldFireEvery(job, now);
        }

        if (job.ScheduleKind == CronScheduleKind.At)
        {
            return ShouldFireAt(job, now);
        }

        return false;
    }

    private static bool ShouldFireCron(CronJob job, DateTimeOffset now)
    {
        if (!CronMatches(job.ScheduleExpr, now))
        {
            return false;
        }

        if (job.LastRunAt is null)
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(job.LastRunAt, null, DateTimeStyles.RoundtripKind, out var lastRun))
        {
            return true;
        }

        var startOfMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset);
        return lastRun < startOfMinute;
    }

    private static bool ShouldFireEvery(CronJob job, DateTimeOffset now)
    {
        if (!long.TryParse(job.ScheduleExpr, out var intervalMs) || intervalMs <= 0)
        {
            return false;
        }

        if (job.LastRunAt is null)
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(job.LastRunAt, null, DateTimeStyles.RoundtripKind, out var lastRun))
        {
            return true;
        }

        return (now - lastRun).TotalMilliseconds >= intervalMs;
    }

    private static bool ShouldFireAt(CronJob job, DateTimeOffset now)
    {
        if (job.LastRunAt is not null)
        {
            return false; // already fired
        }

        return DateTimeOffset.TryParse(job.ScheduleExpr, null, DateTimeStyles.RoundtripKind, out var target)
               && target <= now;
    }

    private static string MakeConfigId(CronEntry entry)
    {
        return "cfg_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(entry.Schedule + "|" + entry.Channel + "|" + entry.Message))
        )[..16].ToLowerInvariant();
    }

    // ── 5-field cron expression parser ────────────────────────────────────────

    internal static bool CronMatches(string schedule, DateTimeOffset time)
    {
        var fields = schedule.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            return false;
        }

        return MatchesField(time.Minute, fields[0], 0, 59)
               && MatchesField(time.Hour, fields[1], 0, 23)
               && MatchesField(time.Day, fields[2], 1, 31)
               && MatchesField(time.Month, fields[3], 1, 12)
               && MatchesField((int)time.DayOfWeek, fields[4], 0, 6);
    }

    private static bool MatchesField(int value, string field, int min, int max)
    {
        foreach (var part in field.Split(','))
        {
            if (MatchesPart(value, part.Trim(), min, max))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPart(int value, string part, int min, int max)
    {
        if (part == "*")
        {
            return true;
        }

        if (part.Contains('/'))
        {
            var slashIdx = part.IndexOf('/', StringComparison.Ordinal);
            var rangePart = part[..slashIdx];
            if (!int.TryParse(part[(slashIdx + 1)..], out var step) || step <= 0)
            {
                return false;
            }

            int rangeMin, rangeMax;
            if (rangePart == "*")
            {
                rangeMin = min;
                rangeMax = max;
            }
            else if (rangePart.Contains('-'))
            {
                if (!TryParseRange(rangePart, out var lo, out var hi))
                {
                    return false;
                }

                rangeMin = lo;
                rangeMax = hi;
            }
            else
            {
                return false;
            }

            if (value < rangeMin || value > rangeMax)
            {
                return false;
            }

            return (value - rangeMin) % step == 0;
        }

        if (part.Contains('-'))
        {
            return TryParseRange(part, out var lo, out var hi) && value >= lo && value <= hi;
        }

        return int.TryParse(part, out var exact) && exact == value;
    }

    private static bool TryParseRange(string range, out int lo, out int hi)
    {
        lo = 0;
        hi = 0;

        var idx = range.IndexOf('-', StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        if (!int.TryParse(range[..idx], out lo))
        {
            return false;
        }

        if (!int.TryParse(range[(idx + 1)..], out hi))
        {
            return false;
        }

        return lo <= hi;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "CronService: {Count} job(s) loaded")]
    private static partial void LogJobsLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "CronService: initialization failed")]
    private static partial void LogInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "CronService: timer active")]
    private static partial void LogTimerActive(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "CronService: no enabled jobs, suspending timer")]
    private static partial void LogSuspendingTimer(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "CronService: added job {Id} [{Kind}:{Expr}]")]
    private static partial void LogJobAdded(ILogger logger, string id, string kind, string expr);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "CronService: removed job {Id}")]
    private static partial void LogJobRemoved(ILogger logger, string id);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "CronService: error firing job {Id}")]
    private static partial void LogJobFireError(ILogger logger, Exception exception, string id);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Cron firing [{Kind}:{Expr}] -> {Channel}: {Preview}")]
    private static partial void LogJobFiring(ILogger logger, string kind, string expr, string channel, string preview);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "CronService: failed to persist run stats for job {Id}")]
    private static partial void LogPersistStatsFailed(ILogger logger, Exception exception, string id);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "CronService: failed to persist job {Id}")]
    private static partial void LogPersistJobFailed(ILogger logger, Exception exception, string id);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "CronService: unknown channel '{Channel}' for job {Id}, skipping")]
    private static partial void LogInvalidChannel(ILogger logger, string channel, string id);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "CronService: job {Id} has invalid timezone '{Tz}', falling back to UTC")]
    private static partial void LogInvalidTimezone(ILogger logger, string id, string tz);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "CronService: failed to reload jobs from store")]
    private static partial void LogStoreReloadFailed(ILogger logger, Exception exception);
}