using Clawsharp.Cost;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Cost.Queries;

/// <summary>
/// Retrieves aggregated cost and cache-savings totals. Optionally filtered
/// by session ID for per-session cost breakdowns.
/// </summary>
[Handler]
public static partial class GetCostSummary
{
    public sealed record Query(string? SessionId = null);

    private static async ValueTask<CostSummary> HandleAsync(
        Query query,
        CostTracker costTracker,
        CancellationToken ct)
    {
        return await costTracker.GetSummaryAsync(query.SessionId, ct);
    }
}