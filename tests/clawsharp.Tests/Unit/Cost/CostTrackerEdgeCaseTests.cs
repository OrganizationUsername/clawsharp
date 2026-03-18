using Clawsharp.Cost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Cost;

/// <summary>
/// Edge-case tests for <see cref="CostTracker"/>:
/// negative token counts, budget bypass via negative costs, and disabled-mode behavior.
/// </summary>
[TestFixture]
public sealed class CostTrackerEdgeCaseTests : IDisposable
{
    private readonly string _tempDir;

    public CostTrackerEdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clawsharp-cost-edge-{Guid.NewGuid():N}");
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

    // ── HIGH-4: Negative token counts bypass CostTracker budget ─────

    [Test]
    public async Task RecordUsageAsync_NegativeInputTokens_OpenAiPath_ClampedToZero()
    {
        // For OpenAI-path models (gpt-4o), CalculateCostWithCaching computes:
        //   regularInput = Math.Max(0, inputTokens - cacheReadTokens)
        // So negative inputTokens are clamped to 0 and produce $0 cost.
        // This means negative input tokens on non-Anthropic models do NOT reduce budget.
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 10.0m,
            MonthlyLimitUsd = 100.0m,
            WarnAtPercent = 80
        });

        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Record legitimate usage: gpt-4o $5/1M input -> 200K = $1.00
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 200_000, outputTokens: 0);

        var summaryBefore = await tracker.GetSummaryAsync();
        summaryBefore.Daily.ShouldBe(1.0m);

        // Negative input tokens on OpenAI path => Math.Max(0, -100K - 0) = 0 => cost = $0
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: -100_000, outputTokens: 0);

        var summaryAfter = await tracker.GetSummaryAsync();
        summaryAfter.Daily.ShouldBe(1.0m,
            "Negative input tokens on OpenAI path are clamped to 0 by Math.Max");
    }

    [Test]
    public async Task RecordUsageAsync_NegativeInputTokens_AnthropicPath_KnownLimitation_ReducesBudget()
    {
        // For Anthropic-path models (claude-*), CalculateCostWithCaching computes:
        //   inputCost = (inputTokens * inputPer1M + ...) / 1M
        // Negative inputTokens directly reduce cost. This is a known limitation.
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 10.0m,
            MonthlyLimitUsd = 100.0m,
            WarnAtPercent = 80
        });

        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // claude-sonnet-4-6: $3/1M input, $15/1M output
        // 1M input = $3.00
        await tracker.RecordUsageAsync("s1", "claude-sonnet-4-6", inputTokens: 1_000_000, outputTokens: 0);

        var summaryBefore = await tracker.GetSummaryAsync();
        summaryBefore.Daily.ShouldBe(3.0m);

        // Negative input tokens: -500K * $3/1M = -$1.50
        await tracker.RecordUsageAsync("s1", "claude-sonnet-4-6", inputTokens: -500_000, outputTokens: 0);

        var summaryAfter = await tracker.GetSummaryAsync();
        summaryAfter.Daily.ShouldBe(1.50m,
            "Known limitation: negative input tokens on Anthropic path reduce budget total");
    }

    [Test]
    public async Task RecordUsageAsync_NegativeOutputTokens_KnownLimitation_ReducesBudget()
    {
        // Output cost = outputTokens * outputPer1M / 1M — no Math.Max clamp.
        // So negative output tokens directly produce negative cost on any model.
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 10.0m,
            MonthlyLimitUsd = 100.0m,
            WarnAtPercent = 80
        });

        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // gpt-4o $15/1M output -> 100K = $1.50
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 0, outputTokens: 100_000);

        var summaryBefore = await tracker.GetSummaryAsync();
        summaryBefore.Daily.ShouldBe(1.50m);

        // Negative output tokens: -50K * $15/1M = -$0.75
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 0, outputTokens: -50_000);

        var summaryAfter = await tracker.GetSummaryAsync();
        summaryAfter.Daily.ShouldBe(0.75m,
            "Known limitation: negative output tokens reduce budget total");
    }

    [Test]
    public async Task RecordUsageAsync_NegativeOutputTokens_KnownLimitation_CanBypassDailyBudgetLimit()
    {
        // Demonstrates the full budget bypass: fill up daily limit with output tokens,
        // then use negative output tokens to reduce it below the limit.
        // Output token cost has no Math.Max clamp, so negative values work on any model.
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 1.0m,
            MonthlyLimitUsd = 100.0m,
            WarnAtPercent = 80
        });

        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Fill daily budget: gpt-4o output 100K * $15/1M = $1.50
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 0, outputTokens: 100_000);

        // Budget should be exceeded
        var result1 = await tracker.CheckBudgetAsync(estimatedCost: 0.01m);
        result1.Status.ShouldBe(BudgetStatus.Exceeded);

        // Negative output tokens bring us back under budget: -100K * $15/1M = -$1.50
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 0, outputTokens: -100_000);

        // Budget is no longer exceeded (daily total: $1.50 - $1.50 = $0.00)
        var result2 = await tracker.CheckBudgetAsync(estimatedCost: 0.01m);
        result2.Status.ShouldBe(BudgetStatus.Allowed,
            "Known limitation: negative output tokens bypass daily budget enforcement");
    }

    [Test]
    public async Task RecordUsageAsync_NegativeCacheTokens_DoesNotProduceNegativeSavings()
    {
        // CostTracker clamps savings to >= 0 via `if (savings > 0)`, but the underlying
        // cost calculation may still produce unexpected results with negative cache tokens.
        var tracker = CreateTracker();
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        await tracker.RecordUsageAsync("s1", "gpt-4o",
            inputTokens: 1000, outputTokens: 0,
            cacheReadTokens: -500, cacheWriteTokens: 0);

        var summary = await tracker.GetSummaryAsync("s1");

        // The CostTracker record's CacheSavingsUsd is clamped to 0 when savings is negative.
        // (savings = -500 * 5 * 0.5 / 1M < 0 → clamped to 0)
        summary.SessionSavings.ShouldBeGreaterThanOrEqualTo(0m,
            "CostTracker clamps negative savings to 0");
    }

    // ── Disabled mode ──────────────────────────────────────────────────

    [Test]
    public async Task RecordUsageAsync_Disabled_DoesNotRecordAnything()
    {
        var tracker = CreateTracker(new CostConfig { Enabled = false });

        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 1_000_000, outputTokens: 500_000);

        var summary = await tracker.GetSummaryAsync();
        summary.Daily.ShouldBe(0m);
        summary.Monthly.ShouldBe(0m);
    }

    // ── Zero tokens ─────────────────────────────────────────────────────

    [Test]
    public async Task RecordUsageAsync_ZeroTokens_RecordsZeroCost()
    {
        var tracker = CreateTracker();
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 0, outputTokens: 0);

        var summary = await tracker.GetSummaryAsync();
        summary.Daily.ShouldBe(0m);
    }

    // ── Concurrent recording ────────────────────────────────────────────

    [Test]
    public async Task RecordUsageAsync_ConcurrentCalls_NoCostLoss()
    {
        // Verify that concurrent RecordUsageAsync calls don't lose cost data
        // due to the internal semaphore-based locking.
        var tracker = CreateTracker();
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        const int parallelism = 20;
        // gpt-4o-mini: $0.15/1M input, $0.60/1M output
        // Each call: 1000 input = $0.00015, 0 output
        var tasks = Enumerable.Range(0, parallelism)
            .Select(i => tracker.RecordUsageAsync($"session-{i}", "gpt-4o-mini", inputTokens: 1000, outputTokens: 0))
            .ToArray();

        await Task.WhenAll(tasks);

        var summary = await tracker.GetSummaryAsync();
        var expectedTotal = parallelism * (1000m * 0.15m / 1_000_000m);
        summary.Daily.ShouldBe(expectedTotal);
    }

    // ── Budget check with negative estimated cost ───────────────────────

    [Test]
    public async Task CheckBudgetAsync_NegativeEstimatedCost_ReducesProjectedTotal()
    {
        // If estimatedCost is negative, projected totals decrease. This is unusual
        // but the code doesn't guard against it.
        var tracker = CreateTracker(new CostConfig
        {
            Enabled = true,
            DailyLimitUsd = 1.0m,
            MonthlyLimitUsd = 10.0m,
            WarnAtPercent = 80
        });
        await tracker.CheckBudgetAsync(estimatedCost: 0m);

        // Fill to near budget
        await tracker.RecordUsageAsync("s1", "gpt-4o", inputTokens: 190_000, outputTokens: 0);

        // Budget warning at 80% ($0.80) — current is $0.95
        var resultPositive = await tracker.CheckBudgetAsync(estimatedCost: 0.01m);
        resultPositive.Status.ShouldBe(BudgetStatus.Warning);

        // Negative estimated cost brings projection below threshold
        var resultNegative = await tracker.CheckBudgetAsync(estimatedCost: -1.0m);
        resultNegative.Status.ShouldBe(BudgetStatus.Allowed,
            "Negative estimated cost reduces projected total below warning threshold");
    }
}
