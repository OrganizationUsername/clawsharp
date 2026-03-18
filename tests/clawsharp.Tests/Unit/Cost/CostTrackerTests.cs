using Clawsharp.Cost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Cost;

public sealed class CostTrackerTests : IDisposable
{
    private readonly string _tempDir;

    public CostTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clawsharp-cost-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private CostTracker CreateTracker(CostConfig? config = null, [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        config ??= new CostConfig { Enabled = true, DailyLimitUsd = 10.0m, MonthlyLimitUsd = 100.0m, WarnAtPercent = 80 };
        var storage = new CostStorage(Path.Combine(_tempDir, $"{testName}.jsonl"));
        return new CostTracker(storage, Options.Create(config), NullLogger<CostTracker>.Instance);
    }

    [Test]
    public async Task CheckBudgetAsync_Disabled_ReturnsAllowed()
    {
        var tracker = CreateTracker(new CostConfig { Enabled = false });

        var result = await tracker.CheckBudgetAsync(estimatedCost: 9999.0m);

        result.Status.ShouldBe(BudgetStatus.Allowed);
    }

    [Test]
    public async Task CheckBudgetAsync_UnderLimit_ReturnsAllowed()
    {
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 100.0m,
            MonthlyLimitUsd = 1000.0m,
            WarnAtPercent = 80
        });

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Record a tiny amount of usage
        await tracker.RecordUsageAsync("test-session", "gpt-4o-mini", 100, 50);

        var result = await tracker.CheckBudgetAsync(estimatedCost: 0m);

        result.Status.ShouldBe(BudgetStatus.Allowed);
    }

    [Test]
    public async Task CheckBudgetAsync_ExceedsDailyLimit_ReturnsExceeded()
    {
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 0.01m, // very low limit: $0.01
            MonthlyLimitUsd = 1000.0m,
            WarnAtPercent = 80
        });

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // gpt-4o: $5/1M input, $15/1M output
        // 10000 input = $0.05 -- already over the $0.01 daily limit
        await tracker.RecordUsageAsync("test-session", "gpt-4o", 10_000, 0);

        var result = await tracker.CheckBudgetAsync(estimatedCost: 0m);

        result.Status.ShouldBe(BudgetStatus.Exceeded);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Daily budget exceeded");
    }

    [Test]
    public async Task CheckBudgetAsync_ApproachingLimit_ReturnsWarning()
    {
        var config = new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 1.00m, // $1.00 daily limit
            MonthlyLimitUsd = 1000.0m,
            WarnAtPercent = 80 // warn at $0.80
        };
        var tracker = CreateTracker(config);

        // Prime the tracker (triggers EnsureInitializedAsync on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // gpt-4o: $5/1M input -> 180_000 tokens = $0.90 (90% of $1.00, above 80% threshold but below 100%)
        await tracker.RecordUsageAsync("test-session", "gpt-4o", 180_000, 0);

        var result = await tracker.CheckBudgetAsync(estimatedCost: 0m);

        result.Status.ShouldBe(BudgetStatus.Warning);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("Approaching daily budget");
    }

    [Test]
    public async Task GetSummaryAsync_MultipleCalls_ReturnsAccurateAggregation()
    {
        var tracker = CreateTracker();

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Record two calls
        await tracker.RecordUsageAsync("session-a", "gpt-4o", 1000, 500);
        await tracker.RecordUsageAsync("session-b", "gpt-4o", 2000, 1000);

        var summary = await tracker.GetSummaryAsync("session-a");

        // gpt-4o: $5/1M input, $15/1M output
        // Call 1: 1000*5/1M + 500*15/1M = 0.005 + 0.0075 = 0.0125
        // Call 2: 2000*5/1M + 1000*15/1M = 0.01 + 0.015 = 0.025
        // Total: 0.0375
        summary.Daily.ShouldBe(0.0375m);
        summary.Monthly.ShouldBe(0.0375m);
        // Session A only: 0.0125
        summary.Session.ShouldBe(0.0125m);
    }

    [Test]
    public async Task RecordUsageAsync_UnknownModel_RecordsZeroCost()
    {
        var tracker = CreateTracker();

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Recording with an unknown model should not throw
        await tracker.RecordUsageAsync("test-session", "totally-unknown-model", 5000, 2000);

        var summary = await tracker.GetSummaryAsync();
        summary.Daily.ShouldBe(0.0m);
    }

    [Test]
    public async Task RecordUsageAsync_AnthropicCacheTokens_CalculatesSavingsCorrectly()
    {
        var tracker = CreateTracker();

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // claude-sonnet-4-6: $3.00/1M input, $15.00/1M output
        // Anthropic cache: write=1.25x, read=0.10x of input price
        // inputTokens=1000 (uncached), outputTokens=200, cacheRead=800, cacheWrite=200
        //
        // inputCost = (1000*3.00 + 200*3.00*1.25 + 800*3.00*0.10) / 1M
        //           = (3000 + 750 + 240) / 1M = 0.003990
        // outputCost = 200*15.00 / 1M = 0.003000
        // totalCost = 0.006990
        // savings = (800*3.00*0.90 - 200*3.00*0.25) / 1M = (2160 - 150) / 1M = 0.002010
        await tracker.RecordUsageAsync(
            "session-anthropic", "claude-sonnet-4-6",
            inputTokens: 1000, outputTokens: 200,
            cacheReadTokens: 800, cacheWriteTokens: 200);

        var summary = await tracker.GetSummaryAsync("session-anthropic");

        summary.Daily.ShouldBe(0.006990m);
        summary.DailySavings.ShouldBe(0.002010m);
        summary.Session.ShouldBe(0.006990m);
        summary.SessionSavings.ShouldBe(0.002010m);
    }

    [Test]
    public async Task RecordUsageAsync_OpenAiCacheTokens_CalculatesSavingsCorrectly()
    {
        var tracker = CreateTracker();

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // gpt-4o: $5.00/1M input, $15.00/1M output
        // OpenAI cache: inputTokens is total (including cached); read=0.50x
        // inputTokens=1000 (total), outputTokens=200, cacheRead=800, cacheWrite=0
        //
        // regularInput = max(0, 1000 - 800) = 200
        // inputCost = (200*5.00 + 800*5.00*0.50) / 1M = (1000 + 2000) / 1M = 0.003000
        // outputCost = 200*15.00 / 1M = 0.003000
        // totalCost = 0.006000
        // savings = 800*5.00*0.50 / 1M = 0.002000
        await tracker.RecordUsageAsync(
            "session-openai", "gpt-4o",
            inputTokens: 1000, outputTokens: 200,
            cacheReadTokens: 800, cacheWriteTokens: 0);

        var summary = await tracker.GetSummaryAsync("session-openai");

        summary.Daily.ShouldBe(0.006000m);
        summary.DailySavings.ShouldBe(0.002000m);
        summary.Session.ShouldBe(0.006000m);
        summary.SessionSavings.ShouldBe(0.002000m);
    }

    [Test]
    public async Task GetSummaryAsync_CacheTokens_ReturnsCorrectSessionAndSavingsFields()
    {
        var tracker = CreateTracker();

        // Prime the tracker (triggers initialization on empty storage).
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Record two calls on different sessions with cache tokens.
        // Session A: Anthropic model with cache tokens
        // claude-sonnet-4-6: $3.00/1M input, $15.00/1M output
        // cost = (500*3.00 + 100*3.00*1.25 + 400*3.00*0.10)/1M + 100*15.00/1M
        //      = (1500 + 375 + 120)/1M + 1500/1M = 1995/1M + 1500/1M = 0.003495
        // savings = (400*3.00*0.90 - 100*3.00*0.25)/1M = (1080 - 75)/1M = 0.001005
        await tracker.RecordUsageAsync(
            "session-A", "claude-sonnet-4-6",
            inputTokens: 500, outputTokens: 100,
            cacheReadTokens: 400, cacheWriteTokens: 100);

        // Session B: OpenAI model with cache tokens
        // gpt-4o: $5.00/1M input, $15.00/1M output
        // regularInput = max(0, 2000 - 1500) = 500
        // cost = (500*5.00 + 1500*5.00*0.50)/1M + 300*15.00/1M
        //      = (2500 + 3750)/1M + 4500/1M = 6250/1M + 4500/1M = 0.010750
        // savings = 1500*5.00*0.50/1M = 0.003750
        await tracker.RecordUsageAsync(
            "session-B", "gpt-4o",
            inputTokens: 2000, outputTokens: 300,
            cacheReadTokens: 1500, cacheWriteTokens: 0);

        // Check session-scoped summary for A only
        var summaryA = await tracker.GetSummaryAsync("session-A");
        summaryA.Session.ShouldBe(0.003495m);
        summaryA.SessionSavings.ShouldBe(0.001005m);

        // Check session-scoped summary for B only
        var summaryB = await tracker.GetSummaryAsync("session-B");
        summaryB.Session.ShouldBe(0.010750m);
        summaryB.SessionSavings.ShouldBe(0.003750m);

        // Daily and monthly totals should include both sessions
        var expectedDailyTotal = 0.003495m + 0.010750m;
        var expectedDailySavings = 0.001005m + 0.003750m;
        summaryA.Daily.ShouldBe(expectedDailyTotal);
        summaryA.Monthly.ShouldBe(expectedDailyTotal);
        summaryA.DailySavings.ShouldBe(expectedDailySavings);
        summaryA.MonthlySavings.ShouldBe(expectedDailySavings);
    }
}
