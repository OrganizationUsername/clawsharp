using Clawsharp.Config;
using Clawsharp.Cost;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Features;

namespace Clawsharp.Cli.Cost;

/// <summary>Displays cost summary: session, daily, and monthly breakdowns.</summary>
[UsedImplicitly]
public sealed class CostShowCommand : AsyncCommand
{
    private sealed record Aggregation(
        decimal DailyTotal, decimal MonthlyTotal, decimal AllTimeTotal,
        long DailyTokensIn, long DailyTokensOut,
        long MonthlyTokensIn, long MonthlyTokensOut,
        decimal DailySavings, decimal MonthlySavings,
        long DailyCacheReads, long MonthlyCacheReads,
        Dictionary<string, (long In, long Out, decimal Cost, int Count, long CacheRead, long CacheWrite, decimal Savings)> ModelStats);

    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var costConfig = config.Cost ?? new CostConfig();

        if (!costConfig.Enabled)
        {
            AnsiConsole.MarkupLine("[yellow]Cost tracking is not enabled.[/]");
            AnsiConsole.MarkupLine("[grey]Set cost.enabled = true in your config to enable it.[/]");
            return 0;
        }

        var storage = new CostStorage();
        var records = await storage.ReadAllAsync(cancellationToken).ConfigureAwait(false);

        if (records.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No cost records found yet.[/]");
            return 0;
        }

        var agg = AggregateRecords(records);

        AnsiConsole.MarkupLine("[bold]Cost Summary[/]");
        AnsiConsole.WriteLine();
        RenderSummaryTable(costConfig, agg);
        RenderModelBreakdownTable(agg.ModelStats);

        return 0;
    }

    private static Aggregation AggregateRecords(IReadOnlyList<CostRecord> records)
    {
        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateOnly.FromDateTime(now.UtcDateTime);

        var dailyTotal = 0.0m;
        var monthlyTotal = 0.0m;
        var allTimeTotal = 0.0m;
        var dailyTokensIn = 0L;
        var dailyTokensOut = 0L;
        var monthlyTokensIn = 0L;
        var monthlyTokensOut = 0L;
        var dailySavings = 0.0m;
        var monthlySavings = 0.0m;
        var dailyCacheReads = 0L;
        var monthlyCacheReads = 0L;

        var modelStats =
            new Dictionary<string, (long In, long Out, decimal Cost, int Count, long CacheRead, long CacheWrite, decimal Savings)>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var r in records)
        {
            allTimeTotal += r.CostUsd;
            var recordDate = DateOnly.FromDateTime(r.Timestamp.UtcDateTime);

            if (recordDate == todayUtc)
            {
                dailyTotal += r.CostUsd;
                dailyTokensIn += r.InputTokens;
                dailyTokensOut += r.OutputTokens;
                dailySavings += r.CacheSavingsUsd;
                dailyCacheReads += r.CacheReadTokens;
            }

            if (r.Timestamp.UtcDateTime.Year == now.UtcDateTime.Year &&
                r.Timestamp.UtcDateTime.Month == now.UtcDateTime.Month)
            {
                monthlyTotal += r.CostUsd;
                monthlyTokensIn += r.InputTokens;
                monthlyTokensOut += r.OutputTokens;
                monthlySavings += r.CacheSavingsUsd;
                monthlyCacheReads += r.CacheReadTokens;

                if (!modelStats.TryGetValue(r.Model, out var stats))
                {
                    stats = (0L, 0L, 0.0m, 0, 0L, 0L, 0.0m);
                }

                modelStats[r.Model] = (
                    stats.In + r.InputTokens,
                    stats.Out + r.OutputTokens,
                    stats.Cost + r.CostUsd,
                    stats.Count + 1,
                    stats.CacheRead + r.CacheReadTokens,
                    stats.CacheWrite + r.CacheWriteTokens,
                    stats.Savings + r.CacheSavingsUsd);
            }
        }

        return new Aggregation(
            dailyTotal, monthlyTotal, allTimeTotal,
            dailyTokensIn, dailyTokensOut,
            monthlyTokensIn, monthlyTokensOut,
            dailySavings, monthlySavings,
            dailyCacheReads, monthlyCacheReads,
            modelStats);
    }

    private static void RenderSummaryTable(CostConfig costConfig, Aggregation agg)
    {
        var hasCacheData = agg.DailyCacheReads > 0 || agg.MonthlyCacheReads > 0;

        var summaryTable = new Table().NoBorder();
        summaryTable.AddColumn(new TableColumn("Period").PadRight(4));
        summaryTable.AddColumn(new TableColumn("Cost (USD)").RightAligned());
        summaryTable.AddColumn(new TableColumn("Limit").RightAligned());
        summaryTable.AddColumn(new TableColumn("Tokens In").RightAligned());
        summaryTable.AddColumn(new TableColumn("Tokens Out").RightAligned());
        if (hasCacheData)
        {
            summaryTable.AddColumn(new TableColumn("Cache Reads").RightAligned());
            summaryTable.AddColumn(new TableColumn("Saved (USD)").RightAligned());
        }

        var dailyLimit = FormatLimit(costConfig.DailyLimitUsd);
        var monthlyLimit = FormatLimit(costConfig.MonthlyLimitUsd);
        var dailyCostColor = GetCostColor(agg.DailyTotal, costConfig.DailyLimitUsd, costConfig.WarnAtPercent);
        var monthlyCostColor = GetCostColor(agg.MonthlyTotal, costConfig.MonthlyLimitUsd, costConfig.WarnAtPercent);

        if (hasCacheData)
        {
            summaryTable.AddRow(
                "Today",
                $"[{dailyCostColor}]${agg.DailyTotal:F4}[/]",
                dailyLimit,
                $"{agg.DailyTokensIn:N0}",
                $"{agg.DailyTokensOut:N0}",
                $"[cyan]{agg.DailyCacheReads:N0}[/]",
                agg.DailySavings > 0 ? $"[green]${agg.DailySavings:F4}[/]" : "[grey]$0.0000[/]");

            summaryTable.AddRow(
                "This month",
                $"[{monthlyCostColor}]${agg.MonthlyTotal:F4}[/]",
                monthlyLimit,
                $"{agg.MonthlyTokensIn:N0}",
                $"{agg.MonthlyTokensOut:N0}",
                $"[cyan]{agg.MonthlyCacheReads:N0}[/]",
                agg.MonthlySavings > 0 ? $"[green]${agg.MonthlySavings:F4}[/]" : "[grey]$0.0000[/]");

            summaryTable.AddRow(
                "All time",
                $"${agg.AllTimeTotal:F4}",
                "[grey]--[/]",
                "[grey]--[/]",
                "[grey]--[/]",
                "[grey]--[/]",
                "[grey]--[/]");
        }
        else
        {
            summaryTable.AddRow(
                "Today",
                $"[{dailyCostColor}]${agg.DailyTotal:F4}[/]",
                dailyLimit,
                $"{agg.DailyTokensIn:N0}",
                $"{agg.DailyTokensOut:N0}");

            summaryTable.AddRow(
                "This month",
                $"[{monthlyCostColor}]${agg.MonthlyTotal:F4}[/]",
                monthlyLimit,
                $"{agg.MonthlyTokensIn:N0}",
                $"{agg.MonthlyTokensOut:N0}");

            summaryTable.AddRow(
                "All time",
                $"${agg.AllTimeTotal:F4}",
                "[grey]--[/]",
                "[grey]--[/]",
                "[grey]--[/]");
        }

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();
    }

    private static string FormatLimit(decimal limitUsd)
    {
        if (limitUsd > 0)
        {
            return $"${limitUsd:F2}";
        }

        return "[grey]none[/]";
    }

    private static string GetCostColor(decimal total, decimal limitUsd, decimal warnAtPercent)
    {
        if (limitUsd > 0 && total > limitUsd)
        {
            return "red";
        }

        if (limitUsd > 0 && total >= limitUsd * warnAtPercent / 100.0m)
        {
            return "yellow";
        }

        return "green";
    }

    private static void RenderModelBreakdownTable(
        Dictionary<string, (long In, long Out, decimal Cost, int Count, long CacheRead, long CacheWrite, decimal Savings)> modelStats)
    {
        if (modelStats.Count == 0)
        {
            return;
        }

        AnsiConsole.MarkupLine("[bold]This Month by Model[/]");
        AnsiConsole.WriteLine();

        var anyModelCacheData = modelStats.Values.Any(s => s.CacheRead > 0 || s.CacheWrite > 0);

        var modelTable = new Table().Border(TableBorder.Simple);
        modelTable.AddColumn("Model");
        modelTable.AddColumn(new TableColumn("Requests").RightAligned());
        modelTable.AddColumn(new TableColumn("Tokens In").RightAligned());
        modelTable.AddColumn(new TableColumn("Tokens Out").RightAligned());
        if (anyModelCacheData)
        {
            modelTable.AddColumn(new TableColumn("Cache Reads").RightAligned());
            modelTable.AddColumn(new TableColumn("Cache Writes").RightAligned());
            modelTable.AddColumn(new TableColumn("Saved (USD)").RightAligned());
        }

        modelTable.AddColumn(new TableColumn("Cost (USD)").RightAligned());

        foreach (var (model, stats) in modelStats.OrderByDescending(kv => kv.Value.Cost))
        {
            if (anyModelCacheData)
            {
                modelTable.AddRow(
                    Markup.Escape(model),
                    stats.Count.ToString("N0"),
                    stats.In.ToString("N0"),
                    stats.Out.ToString("N0"),
                    $"[cyan]{stats.CacheRead:N0}[/]",
                    $"{stats.CacheWrite:N0}",
                    stats.Savings > 0 ? $"[green]${stats.Savings:F4}[/]" : "[grey]$0.0000[/]",
                    $"${stats.Cost:F4}");
            }
            else
            {
                modelTable.AddRow(
                    Markup.Escape(model),
                    stats.Count.ToString("N0"),
                    stats.In.ToString("N0"),
                    stats.Out.ToString("N0"),
                    $"${stats.Cost:F4}");
            }
        }

        AnsiConsole.Write(modelTable);
    }
}