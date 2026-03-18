using Clawsharp.Cost;

namespace Clawsharp.Tests.Unit.Cost;

/// <summary>
/// Pure math scenario tests using DefaultPricing.CalculateCost. No I/O.
/// </summary>
public sealed class CostSimulationTests
{
    [Test]
    public void CalculateCost_LightUserMonthly_UnderOneDollar()
    {
        // 10 requests/day x 30 days = 300 requests
        // gpt-4o-mini: $0.15/1M input, $0.60/1M output
        // avg 200 input + 100 output per request
        var costPerRequest = DefaultPricing.CalculateCost("gpt-4o-mini", 200, 100);
        var monthlyCost = costPerRequest * 300;

        monthlyCost.ShouldBeLessThan(1.0m);
        monthlyCost.ShouldBeGreaterThan(0.0m);
    }

    [Test]
    public void CalculateCost_HeavyUserDailyLimit_ExceededAfterNRequests()
    {
        // claude-opus-4-6: $15/1M input, $75/1M output
        // avg 2000 input + 1000 output per request
        var costPerRequest = DefaultPricing.CalculateCost("claude-opus-4-6", 2000, 1000);
        costPerRequest.ShouldBeGreaterThan(0m);

        // Set a daily limit of 5x a single request cost
        var dailyLimit = costPerRequest * 5;

        // After 6 requests, we should exceed the limit
        var totalCostAfter6 = costPerRequest * 6;
        totalCostAfter6.ShouldBeGreaterThan(dailyLimit);

        // After 5 requests, we are exactly at the limit
        var totalCostAfter5 = costPerRequest * 5;
        totalCostAfter5.ShouldBe(dailyLimit);
    }

    [Test]
    public void CalculateCost_CheapVsExpensiveModel_PriceDifferenceOver100x()
    {
        // gpt-4o-mini: $0.15/1M input, $0.60/1M output — cheapest in list
        // claude-opus-4-6: $15/1M input, $75/1M output — most expensive in list
        var cheapCost = DefaultPricing.CalculateCost("gpt-4o-mini", 1_000_000, 1_000_000);
        var expensiveCost = DefaultPricing.CalculateCost("claude-opus-4-6", 1_000_000, 1_000_000);

        cheapCost.ShouldBeGreaterThan(0m);
        expensiveCost.ShouldBeGreaterThan(0m);

        var ratio = expensiveCost / cheapCost;
        ratio.ShouldBeGreaterThan(100.0m);
    }

    [Test]
    public void CalculateCost_MonthlyProjection_WithinReasonableRange()
    {
        // 20 requests/day x 30 days = 600 requests
        // gpt-4o: $5/1M input, $15/1M output
        // avg 1000 input + 500 output per request
        var costPerRequest = DefaultPricing.CalculateCost("gpt-4o", 1000, 500);
        var monthlyCost = costPerRequest * 600;

        // Should be in a reasonable range: $5-$50
        monthlyCost.ShouldBeGreaterThan(5.0m);
        monthlyCost.ShouldBeLessThan(50.0m);
    }

    [Test]
    public void CalculateCost_AllKnownModels_ReturnsNonNegativeCost()
    {
        string[] knownModels =
        [
            "gpt-4o", "gpt-4o-mini", "o1-preview", "o3-mini", "gpt-4.1", "gpt-4.1-mini", "gpt-5.2",
            "claude-sonnet-4-6", "claude-opus-4-6", "claude-3-haiku",
            "gemini-2.0-flash", "gemini-2.5-pro", "gemini-2.5-flash",
            "deepseek-chat", "deepseek-reasoner",
            "mistral-large-latest",
            "anthropic/claude-sonnet-4-20250514", "anthropic/claude-opus-4-20250514",
            "openai/gpt-4o", "openai/gpt-4o-mini",
            "google/gemini-2.0-flash", "google/gemini-1.5-pro"
        ];

        foreach (var model in knownModels)
        {
            var cost = DefaultPricing.CalculateCost(model, 1000, 1000);
            cost.ShouldBeGreaterThanOrEqualTo(0m, $"Cost for {model} should be >= 0");
        }
    }

    [Test]
    public void CalculateCost_ZeroTokens_ReturnsZeroCost()
    {
        string[] models = ["gpt-4o", "claude-opus-4-6", "deepseek-chat", "totally-unknown"];

        foreach (var model in models)
        {
            var cost = DefaultPricing.CalculateCost(model, 0, 0);
            cost.ShouldBe(0.0m, $"Zero tokens for {model} should cost $0.00");
        }
    }

    // ── Cache-aware pricing ──────────────────────────────────────────────────

    [Test]
    public void CalculateCostWithCaching_AnthropicCacheRead_DiscountedAt10Percent()
    {
        // claude-sonnet-4-6: $3/1M input
        // 1000 cache-read tokens, no regular input, no output
        // Expected cost:  1000 * $3 * 0.10 / 1_000_000
        // Expected savings: 1000 * $3 * 0.90 / 1_000_000
        var (cost, savings) =
            DefaultPricing.CalculateCostWithCaching("claude-sonnet-4-6", 0, 0, cacheReadTokens: 1000, cacheWriteTokens: 0);

        cost.ShouldBe(1000m * 3.00m * 0.10m / 1_000_000m);
        savings.ShouldBe(1000m * 3.00m * 0.90m / 1_000_000m);
    }

    [Test]
    public void CalculateCostWithCaching_AnthropicCacheWrite_ChargedAt125Percent()
    {
        // claude-sonnet-4-6: $3/1M input
        // 1000 cache-write tokens, no regular input, no output
        // Expected cost:  1000 * $3 * 1.25 / 1_000_000
        // Expected savings: negative — write carries a 25% premium
        var (cost, savings) =
            DefaultPricing.CalculateCostWithCaching("claude-sonnet-4-6", 0, 0, cacheReadTokens: 0, cacheWriteTokens: 1000);

        cost.ShouldBe(1000m * 3.00m * 1.25m / 1_000_000m);
        savings.ShouldBe(-1000m * 3.00m * 0.25m / 1_000_000m);
    }

    [Test]
    public void CalculateCostWithCaching_OpenAiCacheRead_DiscountedAt50Percent()
    {
        // gpt-4o: $5/1M input
        // 1000 total prompt tokens, 500 served from cache, no output
        // Regular (uncached) input = 500 tokens at $5/1M
        // Cached input             = 500 tokens at $5 * 0.50 / 1M
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching("gpt-4o", 1000, 0, cacheReadTokens: 500, cacheWriteTokens: 0);

        var expected = (500m * 5.0m + 500m * 5.0m * 0.50m) / 1_000_000m;
        cost.ShouldBe(expected);
        savings.ShouldBe(500m * 5.0m * 0.50m / 1_000_000m);
    }

    [Test]
    public void CalculateCostWithCaching_NoCacheTokens_MatchesBasicCalculateCost()
    {
        // Without cache tokens the result must equal the original CalculateCost overload.
        string[] models = ["claude-sonnet-4-6", "gpt-4o", "gemini-2.0-flash"];

        foreach (var model in models)
        {
            var (cost, savings) = DefaultPricing.CalculateCostWithCaching(model, 1000, 500, 0, 0);
            var basicCost = DefaultPricing.CalculateCost(model, 1000, 500);

            cost.ShouldBe(basicCost, $"{model}: cache-aware cost with zero cache tokens should equal basic cost");
            savings.ShouldBe(0.0m, $"{model}: zero cache tokens should yield zero savings");
        }
    }

    [Test]
    public void CalculateCostWithCaching_UnknownModel_ReturnsZeroCost()
    {
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching("totally-unknown-model", 1000, 500, 200, 100);

        cost.ShouldBe(0.0m);
        savings.ShouldBe(0.0m);
    }

    [Test]
    public void CalculateCostWithCaching_AnthropicTypicalSession_ReadSavingsExceedWritePremium()
    {
        // Scenario from the P5.13 research doc:
        // 1500-token system prompt, 100 msgs/day.
        // Turn 1: write 1500 tokens to cache; turns 2–100: read 1500 tokens from cache.
        // claude-sonnet-4-6: $3/1M input.
        const string model = "claude-sonnet-4-6";
        const int systemTokens = 1500;
        const int msgs = 100;

        var (writeRequestCost, writeRequestSavings) =
            DefaultPricing.CalculateCostWithCaching(model, 0, 0, 0, systemTokens);
        var (readRequestCost, readRequestSavings) =
            DefaultPricing.CalculateCostWithCaching(model, 0, 0, systemTokens, 0);

        // One write + 99 reads
        var totalCost = writeRequestCost + (msgs - 1) * readRequestCost;
        var totalSavings = writeRequestSavings + (msgs - 1) * readRequestSavings;

        // Uncached baseline: all 100 turns pay full input price for the system prompt.
        var uncachedCost = msgs * systemTokens * 3.00m / 1_000_000m;

        // Net savings should be ≈89% of uncached cost.
        var netSavingsPercent = (double)(totalSavings / uncachedCost * 100.0m);
        netSavingsPercent.ShouldBeGreaterThan(85.0);

        totalCost.ShouldBeLessThan(uncachedCost);
    }
}
