using Clawsharp.Cost;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Cost.Queries;

/// <summary>
/// Checks whether an estimated cost would exceed the configured budget limits.
/// Returns immediately with <see cref="BudgetStatus.Allowed"/> if cost tracking is disabled.
/// </summary>
[Handler]
public static partial class CheckBudget
{
    public sealed record Query(decimal EstimatedCost);

    private static async ValueTask<BudgetCheckResult> HandleAsync(
        Query query,
        CostTracker costTracker,
        CancellationToken ct)
    {
        return await costTracker.CheckBudgetAsync(query.EstimatedCost, ct);
    }
}