using Clawsharp.Config.Features;
using Clawsharp.Cost;

namespace Clawsharp.Tests.Unit.Cost;

[TestFixture]
public sealed class DefaultPricingCachingTests
{
    // ── OpenAI caching path: cacheReadTokens > inputTokens (clamp) ────

    [Test]
    public void CalculateCostWithCaching_OpenAi_CacheReadExceedsInput_ClampedToZeroRegular()
    {
        // OpenAI: inputTokens includes cached tokens. If cacheReadTokens > inputTokens,
        // regularInput = Math.Max(0, ...) clamps to 0. Cost must never be negative.
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "gpt-4o",
            inputTokens: 100,
            outputTokens: 0,
            cacheReadTokens: 200,  // More than input — clamped to 0 regular input
            cacheWriteTokens: 0);

        cost.ShouldBeGreaterThanOrEqualTo(0m, "cost must never be negative");
        savings.ShouldBeGreaterThanOrEqualTo(0m, "savings must never be negative on OpenAI path");

        // Expected: regularInput = max(0, 100 - 200) = 0
        //           inputCost = (0 * 5 + 200 * 5 * 0.5) / 1M = 0.0005
        //           (gpt-4o: $5/1M input)
        cost.ShouldBe(200m * 5m * 0.50m / 1_000_000m);
    }

    [Test]
    public void CalculateCostWithCaching_OpenAi_NormalCacheRead_PartialDiscount()
    {
        // 1000 total input, 400 cached, 600 regular
        // gpt-4o: $5/1M
        // inputCost = (600 * 5 + 400 * 5 * 0.5) / 1M = (3000 + 1000) / 1M = 0.004
        // savings = 400 * 5 * 0.5 / 1M = 0.001
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "gpt-4o",
            inputTokens: 1000,
            outputTokens: 0,
            cacheReadTokens: 400,
            cacheWriteTokens: 0);

        cost.ShouldBe(0.004m, 0.0001m);
        savings.ShouldBe(0.001m, 0.0001m);
    }

    // ── Anthropic caching path ─────────────────────────────────────────

    [Test]
    public void CalculateCostWithCaching_Anthropic_WriteOnly_SavingsNegative()
    {
        // Anthropic write-only scenario: cacheWriteTokens > 0, cacheReadTokens = 0
        // savings = (0 * 0.90 - writes * 0.25) / 1M → negative
        // claude-sonnet-4-6: $3/1M input
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "claude-sonnet-4-6",
            inputTokens: 1_000_000,
            outputTokens: 0,
            cacheReadTokens: 0,
            cacheWriteTokens: 1_000_000);

        // inputCost = (1M * 3 + 1M * 3 * 1.25 + 0) / 1M = 3 + 3.75 = 6.75
        cost.ShouldBe(6.75m, 0.01m);
        savings.ShouldBeLessThan(0m, "write-only Anthropic caching costs more — savings must be negative");
    }

    [Test]
    public void CalculateCostWithCaching_Anthropic_ReadOnly_PositiveSavings()
    {
        // claude-sonnet-4-6: $3/1M input
        // 1M reads at 0.1x = $0.30 actual vs $3 uncached
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "claude-sonnet-4-6",
            inputTokens: 0,
            outputTokens: 0,
            cacheReadTokens: 1_000_000,
            cacheWriteTokens: 0);

        // inputCost = (0 * 3 + 0 * 3 * 1.25 + 1M * 3 * 0.10) / 1M = 0.30
        cost.ShouldBe(0.30m, 0.001m);
        savings.ShouldBeGreaterThan(0m, "cache reads save money on Anthropic");
    }

    [Test]
    public void CalculateCostWithCaching_Anthropic_BothReadAndWrite_NetSavingsPositive()
    {
        // More reads than writes → net positive savings
        // claude-sonnet-4-6: $3/1M
        // savings = (1M * 3 * 0.90 - 100K * 3 * 0.25) / 1M = (2.70 - 0.075) = 2.625
        var (_, savings) = DefaultPricing.CalculateCostWithCaching(
            "claude-sonnet-4-6",
            inputTokens: 500_000,
            outputTokens: 0,
            cacheReadTokens: 1_000_000,
            cacheWriteTokens: 100_000);

        savings.ShouldBeGreaterThan(0m);
    }

    // ── Custom override on Anthropic model ────────────────────────────

    [Test]
    public void CalculateCostWithCaching_AnthropicModel_CustomOverride_UsesOverridePrice()
    {
        // When overrides are provided for a claude-* model, the custom price must be
        // used for both cost and savings — not the built-in table price.
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["claude-sonnet-4-6"] = new ModelPricing { Input = 10m, Output = 40m }
        };

        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "claude-sonnet-4-6",
            inputTokens: 1_000_000,
            outputTokens: 0,
            cacheReadTokens: 500_000,
            cacheWriteTokens: 0,
            overrides: overrides);

        // With override price $10/1M:
        // inputCost = (1M * 10 + 0 + 500K * 10 * 0.10) / 1M = 10 + 0.5 = 10.5
        cost.ShouldBe(10.5m, 0.01m);

        // savings = (500K * 10 * 0.90 - 0) / 1M = 4.5
        savings.ShouldBe(4.5m, 0.01m);
    }

    // ── Unknown model returns zero ────────────────────────────────────

    [Test]
    public void CalculateCostWithCaching_UnknownModel_NoOverride_ReturnsBothZero()
    {
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "totally-unknown-xyz",
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheReadTokens: 500_000,
            cacheWriteTokens: 100_000);

        cost.ShouldBe(0m);
        savings.ShouldBe(0m);
    }

    // ── Output tokens only ────────────────────────────────────────────

    [Test]
    public void CalculateCostWithCaching_OutputTokensOnly_CostFromOutputAlone()
    {
        // gpt-4o: $15/1M output
        var (cost, _) = DefaultPricing.CalculateCostWithCaching(
            "gpt-4o",
            inputTokens: 0,
            outputTokens: 1_000_000,
            cacheReadTokens: 0,
            cacheWriteTokens: 0);

        cost.ShouldBe(15m, 0.001m);
    }

    // ── Anthropic dot-notation normalization ──────────────────────────

    [Test]
    public void CalculateCostWithCaching_AnthropicDotNotation_NormalizedCorrectly()
    {
        // "claude-sonnet-4.6" should normalize to "claude-sonnet-4-6"
        var (costDot, _) = DefaultPricing.CalculateCostWithCaching(
            "claude-sonnet-4.6",
            inputTokens: 1_000_000,
            outputTokens: 0,
            cacheReadTokens: 0,
            cacheWriteTokens: 0);

        var (costHyphen, _) = DefaultPricing.CalculateCostWithCaching(
            "claude-sonnet-4-6",
            inputTokens: 1_000_000,
            outputTokens: 0,
            cacheReadTokens: 0,
            cacheWriteTokens: 0);

        costDot.ShouldBe(costHyphen, "dot and hyphen notation must produce identical costs");
    }

    // ── All-zero tokens ───────────────────────────────────────────────

    [Test]
    public void CalculateCostWithCaching_AllZeroTokens_CostAndSavingsZero()
    {
        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "gpt-4o",
            inputTokens: 0,
            outputTokens: 0,
            cacheReadTokens: 0,
            cacheWriteTokens: 0);

        cost.ShouldBe(0m);
        savings.ShouldBe(0m);
    }
}
