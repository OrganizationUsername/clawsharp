namespace Clawsharp.Cost;

/// <summary>Aggregated cost and cache-savings totals returned by <see cref="CostTracker.GetSummaryAsync"/>.</summary>
public sealed record CostSummary(
    decimal Daily,
    decimal Monthly,
    decimal Session,
    decimal DailySavings,
    decimal MonthlySavings,
    decimal SessionSavings);