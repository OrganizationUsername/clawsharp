using System.Collections.Frozen;
using Clawsharp.Config.Features;

namespace Clawsharp.Cost;

/// <summary>
/// Hardcoded pricing table for common LLM models.
/// Prices are in USD per 1 million tokens (input, output).
/// Unknown models return (0, 0) -- fail-safe, never blocks on unknown pricing.
/// </summary>
public static class DefaultPricing
{
    private static readonly FrozenDictionary<string, (decimal InputPer1M, decimal OutputPer1M)> Prices =
        new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase)
        {
            // Anthropic (prefixed)
            ["anthropic/claude-sonnet-4-20250514"] = (3.00m, 15.00m),
            ["anthropic/claude-opus-4-20250514"] = (15.00m, 75.00m),
            ["anthropic/claude-3.5-sonnet"] = (3.00m, 15.00m),
            ["anthropic/claude-3-haiku"] = (0.25m, 1.25m),

            // Anthropic (bare)
            ["claude-sonnet-4-6"] = (3.00m, 15.00m),
            ["claude-opus-4-6"] = (15.00m, 75.00m),
            ["claude-3-haiku"] = (0.25m, 1.25m),

            // OpenAI (prefixed)
            ["openai/gpt-4o"] = (5.00m, 15.00m),
            ["openai/gpt-4o-mini"] = (0.15m, 0.60m),
            ["openai/o1-preview"] = (15.00m, 60.00m),

            // OpenAI (bare)
            ["gpt-4o"] = (5.00m, 15.00m),
            ["gpt-4o-mini"] = (0.15m, 0.60m),
            ["gpt-4.1"] = (2.00m, 8.00m),
            ["gpt-4.1-mini"] = (0.40m, 1.60m),
            ["gpt-5.2"] = (5.00m, 15.00m),
            ["o1-preview"] = (15.00m, 60.00m),
            ["o3-mini"] = (1.10m, 4.40m),

            // Google (prefixed)
            ["google/gemini-2.0-flash"] = (0.10m, 0.40m),
            ["google/gemini-1.5-pro"] = (1.25m, 5.00m),

            // Google (bare)
            ["gemini-2.0-flash"] = (0.10m, 0.40m),
            ["gemini-2.5-pro"] = (1.25m, 10.00m),
            ["gemini-2.5-flash"] = (0.15m, 0.60m),

            // DeepSeek
            ["deepseek-chat"] = (0.27m, 1.10m),
            ["deepseek-reasoner"] = (0.55m, 2.19m),

            // Mistral
            ["mistral-large-latest"] = (2.00m, 6.00m),

            // AWS Bedrock — Amazon Nova
            ["amazon.nova-pro-v1"] = (0.80m, 3.20m),
            ["amazon.nova-lite-v1"] = (0.06m, 0.24m),
            ["amazon.nova-micro-v1"] = (0.035m, 0.14m),
            ["us.amazon.nova-pro-v1"] = (0.80m, 3.20m),
            ["us.amazon.nova-lite-v1"] = (0.06m, 0.24m),
            ["us.amazon.nova-micro-v1"] = (0.035m, 0.14m),

            // xAI Grok
            ["grok-3"] = (3.00m, 15.00m),
            ["grok-3-mini"] = (0.30m, 0.50m),
            ["grok-2"] = (2.00m, 10.00m),

            // Groq
            ["llama-3.3-70b-versatile"] = (0.59m, 0.79m),
            ["llama-3.1-8b-instant"] = (0.05m, 0.08m),
            ["mixtral-8x7b-32768"] = (0.24m, 0.24m),
            ["gemma2-9b-it"] = (0.20m, 0.20m),

            // Fireworks
            ["accounts/fireworks/models/llama-v3p3-70b-instruct"] = (0.90m, 0.90m),
            ["accounts/fireworks/models/llama-v3p1-8b-instruct"] = (0.20m, 0.20m),

            // Cerebras
            ["llama-3.3-70b"] = (0.85m, 1.20m),
            ["llama3.1-8b"] = (0.10m, 0.10m),

            // Zhipu AI / GLM
            ["glm-4.7"] = (0.60m, 2.20m),
            ["glm-4.5"] = (0.55m, 2.00m),
            ["glm-4.5-x"] = (2.20m, 8.90m),
            ["glm-4.5-air"] = (0.20m, 1.10m),
            ["glm-4.6"] = (0.60m, 2.20m),
            ["glm-4-32b-0414-128k"] = (0.10m, 0.10m),
            ["glm-4-plus"] = (0.60m, 2.20m),
            ["glm-4"] = (0.40m, 1.60m),
            ["glm-4-air"] = (0.20m, 1.10m),
            ["glm-4-long"] = (0.07m, 0.07m),

            // DashScope / Qwen (international pricing)
            ["qwen-max"] = (1.60m, 6.40m),
            ["qwen-plus"] = (0.40m, 1.20m),
            ["qwen-turbo"] = (0.05m, 0.20m),
            ["qwen-long"] = (0.07m, 0.29m),
            ["qwen2.5-72b-instruct"] = (0.175m, 0.70m),
            ["qwen2.5-7b-instruct"] = (0.175m, 0.70m),

            // Moonshot AI / Kimi
            ["moonshot-v1-8k"] = (0.20m, 2.00m),
            ["moonshot-v1-32k"] = (1.00m, 3.00m),
            ["moonshot-v1-128k"] = (2.00m, 5.00m),
            ["kimi-k2-0711-preview"] = (0.60m, 2.50m),
            ["kimi-k2-0905-preview"] = (0.60m, 2.50m),
            ["kimi-k2.5"] = (0.60m, 3.00m),
            ["kimi-k2-thinking"] = (0.60m, 2.50m),

            // MiniMax
            ["MiniMax-Text-01"] = (0.20m, 1.10m),
            ["MiniMax-M2"] = (0.255m, 1.00m),
            ["MiniMax-M2.1"] = (0.27m, 0.95m),
            ["MiniMax-M2.5"] = (0.295m, 1.20m),

            // VolcEngine / ByteDance Doubao
            ["doubao-1-5-pro-32k-250115"] = (0.11m, 0.28m),
            ["doubao-1-5-pro-256k-250115"] = (0.69m, 1.24m),
            ["doubao-1-5-lite-32k-250115"] = (0.04m, 0.08m),
            ["doubao-1.5-pro-32k"] = (0.11m, 0.28m),
            ["doubao-1.5-pro-256k"] = (0.69m, 1.24m),
            ["doubao-1.5-lite-32k"] = (0.04m, 0.08m),

            // SiliconFlow (hosted models — slash format)
            ["deepseek-ai/DeepSeek-V3"] = (0.27m, 1.10m),
            ["deepseek-ai/DeepSeek-R1"] = (0.55m, 2.19m),
            ["Qwen/Qwen2.5-72B-Instruct"] = (0.40m, 1.20m),
            ["Qwen/Qwen2.5-7B-Instruct"] = (0.05m, 0.20m),
            ["moonshotai/Kimi-K2-Instruct"] = (0.58m, 2.29m),
            ["MiniMaxAI/MiniMax-M2.5"] = (0.30m, 1.20m),
            ["meta-llama/Meta-Llama-3.1-8B-Instruct"] = (0.06m, 0.06m),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get pricing for a model. Returns (0, 0) for unknown models.
    /// </summary>
    public static (decimal InputPer1M, decimal OutputPer1M) GetPrice(string model)
    {
        // Normalize Anthropic model names: dots → hyphens (e.g., "claude-sonnet-4.6" → "claude-sonnet-4-6")
        // Only apply to claude-* models to avoid breaking other providers (gpt-4.1, gemini-2.5-pro use dots intentionally)
        if (IsAnthropicModel(model) && model.Contains('.'))
        {
            model = model.Replace('.', '-');
        }

        return Prices.GetValueOrDefault(model);
    }

    /// <summary>
    /// Calculate the USD cost for a given model and token counts.
    /// Returns 0.0 for unknown models.
    /// </summary>
    public static decimal CalculateCost(string model, int inputTokens, int outputTokens)
    {
        var (inputPer1M, outputPer1M) = GetPrice(model);
        if (inputPer1M == 0 && outputPer1M == 0)
        {
            return 0.0m;
        }

        return (inputTokens * inputPer1M / 1_000_000m)
               + (outputTokens * outputPer1M / 1_000_000m);
    }

    /// <summary>
    /// Calculate cost using optional custom pricing overrides.
    /// Falls back to the hardcoded pricing table if no override is found.
    /// </summary>
    public static decimal CalculateCost(
        string model,
        int inputTokens,
        int outputTokens,
        IReadOnlyDictionary<string, ModelPricing>? overrides)
    {
        if (overrides is not null &&
            overrides.TryGetValue(model, out var custom))
        {
            return (inputTokens * custom.Input / 1_000_000m)
                   + (outputTokens * custom.Output / 1_000_000m);
        }

        return CalculateCost(model, inputTokens, outputTokens);
    }

    /// <summary>
    /// Calculate cost accounting for prompt cache read/write tokens and return the
    /// estimated savings versus billing all input at the full uncached rate.
    /// <para>
    /// Anthropic (<c>claude-*</c> / <c>anthropic/*</c>):
    /// <c>inputTokens</c> are the regular (non-cached) tokens; cache writes are billed at 1.25×
    /// and cache reads at 0.10× of the input price.
    /// </para>
    /// <para>
    /// OpenAI and all other providers:
    /// <c>inputTokens</c> is the total prompt token count (OpenAI includes cached tokens in this
    /// figure); cache reads are re-billed at 0.50× while the remainder stays at 1×.
    /// </para>
    /// Returns <c>(ActualCostUsd, SavingsUsd)</c> where savings may be negative for Anthropic
    /// if write overhead exceeds read savings within a single request.
    /// </summary>
    public static (decimal Cost, decimal Savings) CalculateCostWithCaching(
        string model,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheWriteTokens,
        IReadOnlyDictionary<string, ModelPricing>? overrides = null)
    {
        decimal inputPer1M, outputPer1M;

        if (overrides is not null && overrides.TryGetValue(model, out var custom))
        {
            inputPer1M = custom.Input;
            outputPer1M = custom.Output;
        }
        else
        {
            (inputPer1M, outputPer1M) = GetPrice(model);
            if (inputPer1M == 0 && outputPer1M == 0)
            {
                return (0.0m, 0.0m);
            }
        }

        var outputCost = outputTokens * outputPer1M / 1_000_000m;

        decimal inputCost, savings;

        if (IsAnthropicModel(model))
        {
            // inputTokens = regular uncached input (Anthropic's usage.input_tokens).
            // Cache writes are billed at 1.25× (premium for populating the cache).
            // Cache reads are billed at 0.10× (deep discount for cache hits).
            inputCost = (inputTokens * inputPer1M
                         + cacheWriteTokens * inputPer1M * 1.25m
                         + cacheReadTokens * inputPer1M * 0.10m)
                        / 1_000_000m;

            // Net savings = discount on reads minus premium on writes.
            savings = (cacheReadTokens * inputPer1M * 0.90m
                       - cacheWriteTokens * inputPer1M * 0.25m)
                      / 1_000_000m;
        }
        else
        {
            // OpenAI (and compatible): inputTokens = total prompt tokens INCLUDING cached.
            // Cached portion is re-priced at 0.50×; the rest stays at 1×.
            var regularInput = Math.Max(0, inputTokens - cacheReadTokens);
            inputCost = (regularInput * inputPer1M + cacheReadTokens * inputPer1M * 0.50m)
                        / 1_000_000m;

            // Savings = 50% discount on the cached tokens.
            savings = cacheReadTokens * inputPer1M * 0.50m / 1_000_000m;
        }

        return (inputCost + outputCost, savings);
    }

    private static bool IsAnthropicModel(string model) =>
        model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase);
}
