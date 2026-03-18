using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;

namespace Clawsharp.Cost;

/// <summary>
/// Singleton service that tracks LLM costs and enforces budget limits.
/// Uses in-memory aggregation with JSONL persistence and day/month boundary resets.
/// </summary>
public sealed partial class CostTracker(
    CostStorage storage,
    IOptions<CostConfig> config,
    ILogger<CostTracker> logger)
{
    private readonly CostConfig _config = config.Value;

    // In-memory aggregation cache
    private decimal _dailyTotal;

    private decimal _monthlyTotal;

    private DateOnly _currentDay;

    private int _currentMonth;

    private int _currentYear;

    private bool _initialized;

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Check budget before a request. Returns immediately if cost tracking is disabled.</summary>
    public async Task<BudgetCheckResult> CheckBudgetAsync(decimal estimatedCost, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return new BudgetCheckResult(BudgetStatus.Allowed);
        }

        await _lock.WaitAsync(ct);
        try
        {
            await EnsureInitializedAsync(ct);
            CheckDayMonthBoundary();

            var projectedDaily = _dailyTotal + estimatedCost;
            var projectedMonthly = _monthlyTotal + estimatedCost;

            // Check hard limits first
            if (_config.DailyLimitUsd > 0 && projectedDaily > _config.DailyLimitUsd)
            {
                LogBudgetExceeded("daily", projectedDaily, _config.DailyLimitUsd);
                return new BudgetCheckResult(
                    BudgetStatus.Exceeded,
                    $"Daily budget exceeded: ${projectedDaily:F4} / ${_config.DailyLimitUsd:F2}",
                    _dailyTotal,
                    _monthlyTotal);
            }

            if (_config.MonthlyLimitUsd > 0 && projectedMonthly > _config.MonthlyLimitUsd)
            {
                LogBudgetExceeded("monthly", projectedMonthly, _config.MonthlyLimitUsd);
                return new BudgetCheckResult(
                    BudgetStatus.Exceeded,
                    $"Monthly budget exceeded: ${projectedMonthly:F4} / ${_config.MonthlyLimitUsd:F2}",
                    _dailyTotal,
                    _monthlyTotal);
            }

            // Check warning thresholds
            var warnFraction = _config.WarnAtPercent / 100.0m;

            if (_config.DailyLimitUsd > 0 && projectedDaily >= _config.DailyLimitUsd * warnFraction)
            {
                LogBudgetWarning("daily", projectedDaily, _config.DailyLimitUsd);
                return new BudgetCheckResult(
                    BudgetStatus.Warning,
                    $"Approaching daily budget: ${projectedDaily:F4} / ${_config.DailyLimitUsd:F2} ({_config.WarnAtPercent}% threshold)",
                    _dailyTotal,
                    _monthlyTotal);
            }

            if (_config.MonthlyLimitUsd > 0 && projectedMonthly >= _config.MonthlyLimitUsd * warnFraction)
            {
                LogBudgetWarning("monthly", projectedMonthly, _config.MonthlyLimitUsd);
                return new BudgetCheckResult(
                    BudgetStatus.Warning,
                    $"Approaching monthly budget: ${projectedMonthly:F4} / ${_config.MonthlyLimitUsd:F2} ({_config.WarnAtPercent}% threshold)",
                    _dailyTotal,
                    _monthlyTotal);
            }

            return new BudgetCheckResult(BudgetStatus.Allowed, CurrentDailyUsd: _dailyTotal, CurrentMonthlyUsd: _monthlyTotal);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Record usage after a response. Calculates cache-aware cost and persists the record.
    /// <para>
    /// Pass <paramref name="cacheReadTokens"/> and <paramref name="cacheWriteTokens"/> from
    /// <see cref="Core.ChatResponse"/> to get accurate cost accounting for prompt cache hits.
    /// Both default to 0 for callers that do not track cache tokens.
    /// </para>
    /// </summary>
    public async Task RecordUsageAsync(
        string sessionId,
        string model,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens = 0,
        long cacheWriteTokens = 0,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens, _config.Prices);

        var cacheSavings = 0.0m;
        if (savings > 0)
        {
            cacheSavings = savings;
        }

        var record = new CostRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            CacheSavingsUsd = cacheSavings,
            CostUsd = cost,
            Timestamp = DateTimeOffset.UtcNow,
        };

        await storage.AppendAsync(record, ct);

        await _lock.WaitAsync(ct);
        try
        {
            CheckDayMonthBoundary();
            _dailyTotal += cost;
            _monthlyTotal += cost;
            LogUsageRecorded(model, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens, cost, savings);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Get current cost and cache-savings summary. Optionally filter by session.</summary>
    public async Task<CostSummary> GetSummaryAsync(
        string? sessionId = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        decimal daily;
        decimal monthly;
        try
        {
            await EnsureInitializedAsync(ct);
            CheckDayMonthBoundary();
            daily = _dailyTotal;
            monthly = _monthlyTotal;
        }
        finally
        {
            _lock.Release();
        }

        // Savings and session totals are not tracked in memory — scan disk for those.
        var session = 0.0m;
        var dailySavings = 0.0m;
        var monthlySavings = 0.0m;
        var sessionSavings = 0.0m;

        var records = await storage.ReadAllAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateOnly.FromDateTime(now.UtcDateTime);

        foreach (var r in records)
        {
            var recordDate = DateOnly.FromDateTime(r.Timestamp.UtcDateTime);

            if (recordDate == todayUtc)
            {
                dailySavings += r.CacheSavingsUsd;
            }

            if (r.Timestamp.UtcDateTime.Year == now.UtcDateTime.Year &&
                r.Timestamp.UtcDateTime.Month == now.UtcDateTime.Month)
            {
                monthlySavings += r.CacheSavingsUsd;
            }

            if (sessionId is not null &&
                string.Equals(r.SessionId, sessionId, StringComparison.Ordinal))
            {
                session += r.CostUsd;
                sessionSavings += r.CacheSavingsUsd;
            }
        }

        return new CostSummary(daily, monthly, session, dailySavings, monthlySavings, sessionSavings);
    }

    /// <summary>Detect day/month boundary crossings and reset in-memory aggregates.</summary>
    private void CheckDayMonthBoundary()
    {
        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateOnly.FromDateTime(now.UtcDateTime);

        if (todayUtc != _currentDay)
        {
            _dailyTotal = 0;
            _currentDay = todayUtc;

            if (now.UtcDateTime.Year != _currentYear || now.UtcDateTime.Month != _currentMonth)
            {
                _monthlyTotal = 0;
                _currentMonth = now.UtcDateTime.Month;
                _currentYear = now.UtcDateTime.Year;
            }
        }
    }

    /// <summary>Lazily load and aggregate from JSONL on first access.</summary>
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        var records = await storage.ReadAllAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateOnly.FromDateTime(now.UtcDateTime);
        _currentDay = todayUtc;
        _currentMonth = now.UtcDateTime.Month;
        _currentYear = now.UtcDateTime.Year;

        foreach (var r in records)
        {
            var recordDate = DateOnly.FromDateTime(r.Timestamp.UtcDateTime);

            if (recordDate == todayUtc)
            {
                _dailyTotal += r.CostUsd;
            }

            if (r.Timestamp.UtcDateTime.Year == now.UtcDateTime.Year &&
                r.Timestamp.UtcDateTime.Month == now.UtcDateTime.Month)
            {
                _monthlyTotal += r.CostUsd;
            }
        }

        _initialized = true;
        LogInitialized(_dailyTotal, _monthlyTotal, records.Count);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Cost tracker initialized: daily=${DailyTotal:F4}, monthly=${MonthlyTotal:F4}, {RecordCount} records")]
    private partial void LogInitialized(decimal dailyTotal, decimal monthlyTotal, int recordCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message =
            "Usage recorded: {Model} in={InputTokens} out={OutputTokens} cacheRead={CacheRead} cacheWrite={CacheWrite} cost=${Cost:F6} savings=${Savings:F6}")]
    private partial void LogUsageRecorded(string model, long inputTokens, long outputTokens, long cacheRead, long cacheWrite, decimal cost,
                                          decimal savings);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Budget {Period} exceeded: projected ${Projected:F4} > limit ${Limit:F2}")]
    private partial void LogBudgetExceeded(string period, decimal projected, decimal limit);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Budget {Period} warning: projected ${Projected:F4} approaching limit ${Limit:F2}")]
    private partial void LogBudgetWarning(string period, decimal projected, decimal limit);
}
