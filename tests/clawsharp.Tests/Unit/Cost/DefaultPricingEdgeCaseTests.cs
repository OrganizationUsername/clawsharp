using Clawsharp.Config.Features;
using Clawsharp.Cost;

namespace Clawsharp.Tests.Unit.Cost;

/// <summary>
/// Edge-case tests for <see cref="DefaultPricing"/>:
/// negative custom pricing, negative token counts, and boundary conditions.
/// </summary>
[TestFixture]
public sealed class DefaultPricingEdgeCaseTests
{
    // ── MED-27: Negative custom pricing ──────────────────────────────────

    [Test]
    public void CalculateCost_NegativeCustomPricing_KnownLimitation_ProducesNegativeCost()
    {
        // Known limitation: negative pricing values are not rejected and produce negative costs.
        // This could enable budget bypass if custom pricing is user-controlled.
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["my-model"] = new() { Input = -5.0m, Output = -10.0m }
        };

        var cost = DefaultPricing.CalculateCost("my-model", 1_000_000, 1_000_000, overrides);

        // -5/1M * 1M + -10/1M * 1M = -5 + -10 = -15
        cost.ShouldBe(-15.0m,
            "Known limitation: negative custom pricing produces negative cost");
    }

    [Test]
    public void CalculateCostWithCaching_NegativeCustomPricing_KnownLimitation_NegativeCost()
    {
        // Same issue via the caching path
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["my-model"] = new() { Input = -5.0m, Output = -10.0m }
        };

        var (cost, savings) = DefaultPricing.CalculateCostWithCaching(
            "my-model",
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheReadTokens: 0,
            cacheWriteTokens: 0,
            overrides: overrides);

        cost.ShouldBeLessThan(0m,
            "Known limitation: negative custom pricing produces negative cost via caching path");
    }

    [Test]
    public void CalculateCost_ZeroCustomPricing_ReturnsZero()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["my-model"] = new() { Input = 0m, Output = 0m }
        };

        var cost = DefaultPricing.CalculateCost("my-model", 1_000_000, 1_000_000, overrides);

        cost.ShouldBe(0m);
    }

    // ── Negative token counts ───────────────────────────────────────────

    [Test]
    public void CalculateCost_NegativeInputTokens_KnownLimitation_ProducesNegativeCost()
    {
        // Known limitation: negative token counts are not validated.
        // gpt-4o: $5/1M input
        var cost = DefaultPricing.CalculateCost("gpt-4o", -1_000_000, 0);

        cost.ShouldBe(-5.0m,
            "Known limitation: negative input tokens produce negative cost");
    }

    [Test]
    public void CalculateCost_NegativeOutputTokens_KnownLimitation_ProducesNegativeCost()
    {
        // gpt-4o: $15/1M output
        var cost = DefaultPricing.CalculateCost("gpt-4o", 0, -1_000_000);

        cost.ShouldBe(-15.0m,
            "Known limitation: negative output tokens produce negative cost");
    }

    // ── Large token counts (overflow safety) ────────────────────────────

    [Test]
    public void CalculateCost_VeryLargeTokenCounts_NoOverflow()
    {
        // Use int.MaxValue — decimal arithmetic should handle this without overflow
        var cost = DefaultPricing.CalculateCost("gpt-4o-mini", int.MaxValue, int.MaxValue);

        cost.ShouldBeGreaterThan(0m, "Very large token counts should not overflow");
    }

    [Test]
    public void CalculateCostWithCaching_LargeTokenCounts_NoOverflow()
    {
        var (cost, _) = DefaultPricing.CalculateCostWithCaching(
            "gpt-4o",
            inputTokens: long.MaxValue / 1000,
            outputTokens: long.MaxValue / 1000,
            cacheReadTokens: 0,
            cacheWriteTokens: 0);

        cost.ShouldBeGreaterThan(0m, "Large long token counts should not overflow");
    }

    // ── Custom pricing override does not affect other models ────────────

    [Test]
    public void CalculateCost_OverrideForOneModel_DoesNotAffectOtherModels()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["gpt-4o"] = new() { Input = 99.0m, Output = 99.0m }
        };

        // gpt-4o should use the override
        var overriddenCost = DefaultPricing.CalculateCost("gpt-4o", 1_000_000, 0, overrides);
        overriddenCost.ShouldBe(99.0m);

        // gpt-4o-mini should use the built-in price ($0.15/1M input)
        var normalCost = DefaultPricing.CalculateCost("gpt-4o-mini", 1_000_000, 0, overrides);
        normalCost.ShouldBe(0.15m);
    }

    // ── Null overrides dictionary ───────────────────────────────────────

    [Test]
    public void CalculateCost_NullOverrides_UsesBuiltInPricing()
    {
        var cost = DefaultPricing.CalculateCost("gpt-4o", 1_000_000, 0, null);
        cost.ShouldBe(5.0m, "Null overrides should fall through to built-in pricing");
    }

    [Test]
    public void CalculateCostWithCaching_NullOverrides_UsesBuiltInPricing()
    {
        var (cost, _) = DefaultPricing.CalculateCostWithCaching(
            "gpt-4o",
            inputTokens: 1_000_000,
            outputTokens: 0,
            cacheReadTokens: 0,
            cacheWriteTokens: 0,
            overrides: null);

        cost.ShouldBe(5.0m);
    }
}
