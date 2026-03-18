using System.Text;
using System.Text.Json;
using Clawsharp.Analytics;

namespace Clawsharp.Tools.Ops;

public sealed class InteractionsTool(IInteractionStore store) : Tool
{
    private const int MaxPreviewLength = 80;
    public override string Name => "interactions";
    public override string Description => "Query interaction analytics — cost, cache savings, prompt/response data, and usage patterns across sessions, models, and channels.";
    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string ParametersSchemaJson => """
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Query type: 'summary' for aggregate stats, 'recent' for last 10 interactions, 'session:<id>' for session stats, 'model:<name>' for model stats, 'savings' for cache savings breakdown, 'daily' for daily breakdown"
                }
            },
            "required": ["query"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.GetProperty("query").GetString() ?? "summary";
        var records = await store.ReadAllAsync(ct);

        if (records.Count == 0)
        {
            return "No interaction records found.";
        }

        if (query.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
        {
            var sessionId = query[8..];
            return FormatSessionStats(records, sessionId);
        }

        if (query.StartsWith("model:", StringComparison.OrdinalIgnoreCase))
        {
            var modelName = query[6..];
            return FormatModelStats(records, modelName);
        }

        return query.ToLowerInvariant() switch
        {
            "summary" => FormatSummary(records),
            "recent" => FormatRecent(records),
            "savings" => FormatSavings(records),
            "daily" => FormatDaily(records),
            _ => FormatSummary(records),
        };
    }

    private static string FormatSummary(IReadOnlyList<InteractionRecord> records)
    {
        var totalCost = records.Sum(r => r.CostUsd);
        var totalSavings = records.Sum(r => r.CacheSavingsUsd);
        var totalInput = records.Sum(r => r.InputTokens);
        var totalOutput = records.Sum(r => r.OutputTokens);
        var totalCacheRead = records.Sum(r => r.CacheReadTokens);
        var avgDuration = records.Average(r => r.DurationMs);
        var cacheHitRate = totalInput > 0 ? (double)totalCacheRead / totalInput * 100 : 0;

        var modelGroups = records.GroupBy(r => r.Model)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"  {g.Key}: {g.Count()} calls, ${g.Sum(r => r.CostUsd):F4}");

        var channelGroups = records.GroupBy(r => r.Channel)
            .OrderByDescending(g => g.Count())
            .Select(g => $"  {g.Key}: {g.Count()} calls");

        var sessionCount = records.Select(r => r.SessionId).Distinct().Count();

        var sb = new StringBuilder();
        sb.AppendLine($"Total interactions: {records.Count}");
        sb.AppendLine($"Unique sessions: {sessionCount}");
        sb.AppendLine($"Total cost: ${totalCost:F4}");
        sb.AppendLine($"Total cache savings: ${totalSavings:F4}");
        sb.AppendLine($"Net cost (cost - savings): ${totalCost - totalSavings:F4}");
        sb.AppendLine($"Total tokens: {totalInput:N0} in / {totalOutput:N0} out");
        sb.AppendLine($"Cache hit rate: {cacheHitRate:F1}%");
        sb.AppendLine($"Avg response time: {avgDuration:F0}ms");
        sb.AppendLine($"Top models:\n{string.Join("\n", modelGroups)}");
        sb.AppendLine($"Channels:\n{string.Join("\n", channelGroups)}");
        return sb.ToString();
    }

    private static string FormatRecent(IReadOnlyList<InteractionRecord> records)
    {
        var recent = records.OrderByDescending(r => r.Timestamp).Take(10);
        var sb = new StringBuilder();
        foreach (var r in recent)
        {
            var prompt = r.UserPrompt;
            if (r.UserPrompt.Length > MaxPreviewLength)
            {
                prompt = r.UserPrompt[..MaxPreviewLength] + "...";
            }

            var response = r.Response;
            if (r.Response.Length > MaxPreviewLength)
            {
                response = r.Response[..MaxPreviewLength] + "...";
            }

            sb.AppendLine($"[{r.Timestamp:MM-dd HH:mm}] {r.SessionId} ({r.Model})");
            sb.AppendLine($"  Prompt: {prompt}");
            sb.AppendLine($"  Response: {response}");
            sb.AppendLine($"  Cost: ${r.CostUsd:F4} | Savings: ${r.CacheSavingsUsd:F4} | {r.DurationMs}ms");
            if (r.ToolCalls is { Count: > 0 })
            {
                sb.AppendLine($"  Tools: {string.Join(", ", r.ToolCalls.Select(t => t.Name))} ({r.ToolIterations} iterations)");
            }

            if (r.Thinking is { Length: > 0 })
            {
                sb.AppendLine($"  Thinking: {r.Thinking.Length:N0} chars");
            }

            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatSessionStats(IReadOnlyList<InteractionRecord> records, string sessionId)
    {
        var sessionRecords = records.Where(r => r.SessionId.Contains(sessionId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sessionRecords.Count == 0)
        {
            return $"No interactions found for session matching '{sessionId}'.";
        }

        var totalCost = sessionRecords.Sum(r => r.CostUsd);
        var totalSavings = sessionRecords.Sum(r => r.CacheSavingsUsd);
        var totalInput = sessionRecords.Sum(r => r.InputTokens);
        var totalCacheRead = sessionRecords.Sum(r => r.CacheReadTokens);
        var cacheRate = totalInput > 0 ? (double)totalCacheRead / totalInput * 100 : 0;
        var toolUseCount = sessionRecords.Count(r => r.ToolCalls is { Count: > 0 });

        var sb = new StringBuilder();
        sb.AppendLine($"Session: {sessionRecords[0].SessionId}");
        sb.AppendLine($"Interactions: {sessionRecords.Count}");
        sb.AppendLine($"Total cost: ${totalCost:F4}");
        sb.AppendLine($"Total savings: ${totalSavings:F4}");
        sb.AppendLine($"Cache hit rate: {cacheRate:F1}%");
        sb.AppendLine($"Tool use: {toolUseCount}/{sessionRecords.Count} interactions used tools");
        sb.AppendLine($"Models: {string.Join(", ", sessionRecords.Select(r => r.Model).Distinct())}");
        sb.AppendLine($"Time range: {sessionRecords.Min(r => r.Timestamp):g} — {sessionRecords.Max(r => r.Timestamp):g}");
        return sb.ToString();
    }

    private static string FormatModelStats(IReadOnlyList<InteractionRecord> records, string modelName)
    {
        var modelRecords = records.Where(r => r.Model.Contains(modelName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (modelRecords.Count == 0)
        {
            return $"No interactions found for model matching '{modelName}'.";
        }

        var totalCost = modelRecords.Sum(r => r.CostUsd);
        var totalSavings = modelRecords.Sum(r => r.CacheSavingsUsd);
        var totalInput = modelRecords.Sum(r => r.InputTokens);
        var totalOutput = modelRecords.Sum(r => r.OutputTokens);
        var totalCacheRead = modelRecords.Sum(r => r.CacheReadTokens);
        var cacheRate = totalInput > 0 ? (double)totalCacheRead / totalInput * 100 : 0;
        var avgDuration = modelRecords.Average(r => r.DurationMs);

        var sb = new StringBuilder();
        sb.AppendLine($"Model: {modelRecords[0].Model}");
        sb.AppendLine($"Interactions: {modelRecords.Count}");
        sb.AppendLine($"Total cost: ${totalCost:F4}");
        sb.AppendLine($"Total savings: ${totalSavings:F4}");
        sb.AppendLine($"Tokens: {totalInput:N0} in / {totalOutput:N0} out");
        sb.AppendLine($"Cache hit rate: {cacheRate:F1}%");
        sb.AppendLine($"Avg response time: {avgDuration:F0}ms");
        sb.AppendLine($"Sessions: {modelRecords.Select(r => r.SessionId).Distinct().Count()}");
        return sb.ToString();
    }

    private static string FormatSavings(IReadOnlyList<InteractionRecord> records)
    {
        var modelSavings = records.GroupBy(r => r.Model)
            .Select(g => new
            {
                Model = g.Key,
                Cost = g.Sum(r => r.CostUsd),
                Savings = g.Sum(r => r.CacheSavingsUsd),
                CacheReads = g.Sum(r => r.CacheReadTokens),
                TotalInput = g.Sum(r => r.InputTokens),
            })
            .OrderByDescending(x => x.Savings);

        var sb = new StringBuilder();
        sb.AppendLine("Cache savings by model:");
        foreach (var m in modelSavings)
        {
            var rate = m.TotalInput > 0 ? (double)m.CacheReads / m.TotalInput * 100 : 0;
            sb.AppendLine($"  {m.Model}: ${m.Savings:F4} saved (${m.Cost:F4} cost, {rate:F1}% cache hit)");
        }

        var totalCost = records.Sum(r => r.CostUsd);
        var totalSavings = records.Sum(r => r.CacheSavingsUsd);
        var potentialCost = totalCost + totalSavings;
        var reductionPct = potentialCost > 0 ? totalSavings / potentialCost * 100 : 0;
        sb.AppendLine($"\nTotal: ${totalSavings:F4} saved out of ${potentialCost:F4} potential cost ({reductionPct:F1}% reduction)");
        return sb.ToString();
    }

    private static string FormatDaily(IReadOnlyList<InteractionRecord> records)
    {
        var daily = records.GroupBy(r => r.Timestamp.Date)
            .OrderByDescending(g => g.Key)
            .Take(14);

        var sb = new StringBuilder();
        sb.AppendLine("Daily breakdown (last 14 days):");
        foreach (var day in daily)
        {
            var cost = day.Sum(r => r.CostUsd);
            var savings = day.Sum(r => r.CacheSavingsUsd);
            sb.AppendLine($"  {day.Key:yyyy-MM-dd}: {day.Count()} interactions, ${cost:F4} cost, ${savings:F4} savings");
        }
        return sb.ToString();
    }
}
