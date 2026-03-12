using Clawsharp.Cost;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Cost;

public sealed class DefaultPricingTests
{
    [Test]
    public void CalculateCost_KnownModel_ReturnsCorrectCost()
    {
        // gpt-4o: $5/1M input, $15/1M output
        // 1000 input + 500 output = $0.005 + $0.0075 = $0.0125
        var cost = DefaultPricing.CalculateCost("gpt-4o", 1000, 500);
        cost.ShouldBe(0.0125, tolerance: 0.0001);
    }

    [Test]
    public void CalculateCost_UnknownModel_ReturnsZero()
    {
        var cost = DefaultPricing.CalculateCost("totally-unknown-model-xyz", 5000, 2000);
        cost.ShouldBe(0.0);
    }

    [Test]
    public void CalculateCost_CaseInsensitive_MatchesAnyCase()
    {
        var lower = DefaultPricing.CalculateCost("gpt-4o", 1000, 1000);
        var upper = DefaultPricing.CalculateCost("GPT-4O", 1000, 1000);
        var mixed = DefaultPricing.CalculateCost("Gpt-4o", 1000, 1000);

        lower.ShouldBeGreaterThan(0);
        upper.ShouldBe(lower);
        mixed.ShouldBe(lower);
    }

    [Test]
    public void CalculateCost_ZeroTokens_ReturnsZero()
    {
        var cost = DefaultPricing.CalculateCost("gpt-4o", 0, 0);
        cost.ShouldBe(0.0);
    }

    [Test]
    public void CalculateCost_OnlyInputTokens_ReturnsInputCostOnly()
    {
        // gpt-4o-mini: $0.15/1M input
        // 1_000_000 input tokens = $0.15
        var cost = DefaultPricing.CalculateCost("gpt-4o-mini", 1_000_000, 0);
        cost.ShouldBe(0.15, tolerance: 0.001);
    }

    [Test]
    public void CalculateCost_OnlyOutputTokens_ReturnsOutputCostOnly()
    {
        // gpt-4o-mini: $0.60/1M output
        // 1_000_000 output tokens = $0.60
        var cost = DefaultPricing.CalculateCost("gpt-4o-mini", 0, 1_000_000);
        cost.ShouldBe(0.60, tolerance: 0.001);
    }

    [Test]
    public void CalculateCost_CustomPriceOverride_TakesPrecedence()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["gpt-4o"] = new() { Input = 99.0, Output = 199.0 }
        };

        // 1000 input at $99/1M = $0.099; 1000 output at $199/1M = $0.199
        var cost = DefaultPricing.CalculateCost("gpt-4o", 1000, 1000, overrides);
        cost.ShouldBe(0.298, tolerance: 0.001);
    }

    [Test]
    public void CalculateCost_CustomOverrideForUnknownModel_Works()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["my-custom-model"] = new() { Input = 1.0, Output = 2.0 }
        };

        // 1_000_000 input = $1.00; 1_000_000 output = $2.00
        var cost = DefaultPricing.CalculateCost("my-custom-model", 1_000_000, 1_000_000, overrides);
        cost.ShouldBe(3.0, tolerance: 0.001);
    }

    [Test]
    public void GetPrice_AllKnownModels_ReturnsNonNegative()
    {
        // Spot-check a representative set of known models
        string[] knownModels =
        [
            "gpt-4o", "gpt-4o-mini", "o1-preview", "o3-mini",
            "claude-sonnet-4-6", "claude-opus-4-6", "claude-3-haiku",
            "gemini-2.0-flash", "gemini-2.5-pro",
            "deepseek-chat", "deepseek-reasoner",
            "mistral-large-latest"
        ];

        foreach (var model in knownModels)
        {
            var (input, output) = DefaultPricing.GetPrice(model);
            input.ShouldBeGreaterThanOrEqualTo(0, $"Input price for {model} should be >= 0");
            output.ShouldBeGreaterThanOrEqualTo(0, $"Output price for {model} should be >= 0");
            // All known models should have at least one non-zero price
            (input + output).ShouldBeGreaterThan(0, $"Model {model} should have non-zero pricing");
        }
    }

    [Test]
    public void CalculateCost_ClaudeOpus_ExpensiveModel()
    {
        // claude-opus-4-6: $15/1M input, $75/1M output
        // 10000 input + 5000 output = $0.15 + $0.375 = $0.525
        var cost = DefaultPricing.CalculateCost("claude-opus-4-6", 10_000, 5_000);
        cost.ShouldBe(0.525, tolerance: 0.001);
    }

    [Test]
    public void CalculateCost_DeepSeekChat_BudgetModel()
    {
        // deepseek-chat: $0.27/1M input, $1.10/1M output
        // 100000 input + 50000 output = $0.027 + $0.055 = $0.082
        var cost = DefaultPricing.CalculateCost("deepseek-chat", 100_000, 50_000);
        cost.ShouldBe(0.082, tolerance: 0.001);
    }
}