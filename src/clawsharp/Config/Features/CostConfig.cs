using System.ComponentModel.DataAnnotations;

namespace Clawsharp.Config.Features;

/// <summary>Cost tracking and budget enforcement configuration.</summary>
public sealed class CostConfig
{
    /// <summary>Whether cost tracking is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Maximum daily spend in USD. 0 disables the daily limit.</summary>
    public double DailyLimitUsd { get; init; } = 10.0;

    /// <summary>Maximum monthly spend in USD. 0 disables the monthly limit.</summary>
    public double MonthlyLimitUsd { get; init; } = 100.0;

    /// <summary>Percentage of limit at which to issue a warning (1-200).</summary>
    [Range(1, 200)]
    public int WarnAtPercent { get; init; } = 80;

    /// <summary>
    /// Custom model pricing overrides (per 1M tokens).
    /// Keys are model names; values specify input and output costs per 1M tokens.
    /// Overrides the hardcoded pricing table in <see cref="Cost.DefaultPricing"/>.
    /// </summary>
    public Dictionary<string, ModelPricing>? Prices { get; init; }
}

/// <summary>Custom pricing for a single model (USD per 1M tokens).</summary>
public sealed class ModelPricing
{
    /// <summary>Cost per 1M input tokens in USD.</summary>
    public double Input { get; init; }

    /// <summary>Cost per 1M output tokens in USD.</summary>
    public double Output { get; init; }
}