using Clawsharp.Cost;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Cost.Commands;

/// <summary>
/// Records LLM token usage and cost after a response. Calculates cache-aware
/// pricing and persists the cost record to the JSONL store.
/// </summary>
[Handler]
public static partial class RecordUsage
{
    public sealed record Command(
        string SessionId,
        string Model,
        long InputTokens,
        long OutputTokens,
        long CacheReadTokens = 0,
        long CacheWriteTokens = 0,
        decimal? ProviderReportedCost = null);

    private static async ValueTask HandleAsync(
        Command command,
        CostTracker costTracker,
        CancellationToken ct)
    {
        await costTracker.RecordUsageAsync(
            command.SessionId,
            command.Model,
            command.InputTokens,
            command.OutputTokens,
            command.CacheReadTokens,
            command.CacheWriteTokens,
            command.ProviderReportedCost,
            ct);
    }
}