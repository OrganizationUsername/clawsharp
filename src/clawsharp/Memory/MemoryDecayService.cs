using Clawsharp.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Memory;

namespace Clawsharp.Memory;

/// <summary>
///     Background service that periodically prunes expired facts based on the
///     configured TTL. Uses exponential-decay scoring for soft relevance loss
///     and hard deletion for facts exceeding the TTL threshold.
/// </summary>
public sealed partial class MemoryDecayService(IMemory memory, IOptions<MemoryConfig> config, ILogger<MemoryDecayService> logger)
    : LifecycleBackgroundService
{
    private readonly MemoryConfig _config = config.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var decay = _config.Decay;
        if (decay is null || !decay.Enabled || decay.TtlDays <= 0)
        {
            LogDecayDisabled();
            return;
        }

        LogDecayStarted(decay.TtlDays, decay.PruneIntervalHours, decay.HalfLifeDays);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(decay.PruneIntervalHours), ct);

                var pruned = await memory.PruneExpiredFactsAsync(TimeSpan.FromDays(decay.TtlDays), ct);
                if (pruned > 0)
                {
                    LogPruned(pruned, decay.TtlDays);
                }
                else
                {
                    LogNoPrunedFacts();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPruneError(ex);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "MemoryDecayService started [ttl={TtlDays}d, interval={IntervalHours}h, halfLife={HalfLifeDays}d]")]
    private partial void LogDecayStarted(int ttlDays, int intervalHours, double halfLifeDays);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Pruned {Count} expired facts (older than {TtlDays} days)")]
    private partial void LogPruned(int count, int ttlDays);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Memory decay is disabled, service will not run")]
    private partial void LogDecayDisabled();

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "MemoryDecayService prune error")]
    private partial void LogPruneError(Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "No expired facts to prune")]
    private partial void LogNoPrunedFacts();
}