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
        var records = await storage.ReadAllAsync(cancellationToken);

        if (records.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No cost records found yet.[/]");
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateOnly.FromDateTime(now.UtcDateTime);

        var dailyTotal = 0.0;
        var monthlyTotal = 0.0;
        var allTimeTotal = 0.0;
        var dailyTokensIn = 0L;
        var dailyTokensOut = 0L;
        var monthlyTokensIn = 0L;
        var monthlyTokensOut = 0L;
        var dailySavings = 0.0;
        var monthlySavings = 0.0;
        var dailyCacheReads = 0L;
        var monthlyCacheReads = 0L;

        // Per-model aggregation for the table
        var modelStats =
            new Dictionary<string, (long In, long Out, double Cost, int Count, long CacheRead, long CacheWrite, double Savings)>(
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
                    stats = (0L, 0L, 0.0, 0, 0L, 0L, 0.0);
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

        // Summary
        AnsiConsole.MarkupLine("[bold]Cost Summary[/]");
        AnsiConsole.WriteLine();

        var hasCacheData = dailyCacheReads > 0 || monthlyCacheReads > 0;

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

        var dailyLimit = costConfig.DailyLimitUsd > 0
            ? $"${costConfig.DailyLimitUsd:F2}"
            : "[grey]none[/]";
        var monthlyLimit = costConfig.MonthlyLimitUsd > 0
            ? $"${costConfig.MonthlyLimitUsd:F2}"
            : "[grey]none[/]";

        var dailyCostColor = costConfig.DailyLimitUsd > 0 && dailyTotal > costConfig.DailyLimitUsd
            ? "red"
            : costConfig.DailyLimitUsd > 0 && dailyTotal >= costConfig.DailyLimitUsd * costConfig.WarnAtPercent / 100.0
                ? "yellow"
                : "green";

        var monthlyCostColor = costConfig.MonthlyLimitUsd > 0 && monthlyTotal > costConfig.MonthlyLimitUsd
            ? "red"
            : costConfig.MonthlyLimitUsd > 0 && monthlyTotal >= costConfig.MonthlyLimitUsd * costConfig.WarnAtPercent / 100.0
                ? "yellow"
                : "green";

        if (hasCacheData)
        {
            summaryTable.AddRow(
                "Today",
                $"[{dailyCostColor}]${dailyTotal:F4}[/]",
                dailyLimit,
                $"{dailyTokensIn:N0}",
                $"{dailyTokensOut:N0}",
                $"[cyan]{dailyCacheReads:N0}[/]",
                dailySavings > 0 ? $"[green]${dailySavings:F4}[/]" : "[grey]$0.0000[/]");

            summaryTable.AddRow(
                "This month",
                $"[{monthlyCostColor}]${monthlyTotal:F4}[/]",
                monthlyLimit,
                $"{monthlyTokensIn:N0}",
                $"{monthlyTokensOut:N0}",
                $"[cyan]{monthlyCacheReads:N0}[/]",
                monthlySavings > 0 ? $"[green]${monthlySavings:F4}[/]" : "[grey]$0.0000[/]");

            summaryTable.AddRow(
                "All time",
                $"${allTimeTotal:F4}",
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
                $"[{dailyCostColor}]${dailyTotal:F4}[/]",
                dailyLimit,
                $"{dailyTokensIn:N0}",
                $"{dailyTokensOut:N0}");

            summaryTable.AddRow(
                "This month",
                $"[{monthlyCostColor}]${monthlyTotal:F4}[/]",
                monthlyLimit,
                $"{monthlyTokensIn:N0}",
                $"{monthlyTokensOut:N0}");

            summaryTable.AddRow(
                "All time",
                $"${allTimeTotal:F4}",
                "[grey]--[/]",
                "[grey]--[/]",
                "[grey]--[/]");
        }

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Per-model breakdown (this month)
        if (modelStats.Count > 0)
        {
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

        return 0;
    }
}