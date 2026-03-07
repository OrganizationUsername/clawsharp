namespace Clawsharp.Cost;

/// <summary>Aggregated cost and cache-savings totals returned by <see cref="CostTracker.GetSummaryAsync"/>.</summary>
public sealed record CostSummary(
    double Daily,
    double Monthly,
    double Session,
    double DailySavings,
    double MonthlySavings,
    double SessionSavings);