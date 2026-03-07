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
    private static readonly FrozenDictionary<string, (double InputPer1M, double OutputPer1M)> Prices =
        new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            // Anthropic (prefixed)
            ["anthropic/claude-sonnet-4-20250514"] = (3.00, 15.00),
            ["anthropic/claude-opus-4-20250514"] = (15.00, 75.00),
            ["anthropic/claude-3.5-sonnet"] = (3.00, 15.00),
            ["anthropic/claude-3-haiku"] = (0.25, 1.25),

            // Anthropic (bare)
            ["claude-sonnet-4-6"] = (3.00, 15.00),
            ["claude-opus-4-6"] = (15.00, 75.00),
            ["claude-3-haiku"] = (0.25, 1.25),

            // OpenAI (prefixed)
            ["openai/gpt-4o"] = (5.00, 15.00),
            ["openai/gpt-4o-mini"] = (0.15, 0.60),
            ["openai/o1-preview"] = (15.00, 60.00),

            // OpenAI (bare)
            ["gpt-4o"] = (5.00, 15.00),
            ["gpt-4o-mini"] = (0.15, 0.60),
            ["gpt-4.1"] = (2.00, 8.00),
            ["gpt-4.1-mini"] = (0.40, 1.60),
            ["gpt-5.2"] = (5.00, 15.00),
            ["o1-preview"] = (15.00, 60.00),
            ["o3-mini"] = (1.10, 4.40),

            // Google (prefixed)
            ["google/gemini-2.0-flash"] = (0.10, 0.40),
            ["google/gemini-1.5-pro"] = (1.25, 5.00),

            // Google (bare)
            ["gemini-2.0-flash"] = (0.10, 0.40),
            ["gemini-2.5-pro"] = (1.25, 10.00),
            ["gemini-2.5-flash"] = (0.15, 0.60),

            // DeepSeek
            ["deepseek-chat"] = (0.27, 1.10),
            ["deepseek-reasoner"] = (0.55, 2.19),

            // Mistral
            ["mistral-large-latest"] = (2.00, 6.00),

            // AWS Bedrock — Amazon Nova
            ["amazon.nova-pro-v1"] = (0.80, 3.20),
            ["amazon.nova-lite-v1"] = (0.06, 0.24),
            ["amazon.nova-micro-v1"] = (0.035, 0.14),
            ["us.amazon.nova-pro-v1"] = (0.80, 3.20),
            ["us.amazon.nova-lite-v1"] = (0.06, 0.24),
            ["us.amazon.nova-micro-v1"] = (0.035, 0.14),

            // xAI Grok
            ["grok-3"] = (3.00, 15.00),
            ["grok-3-mini"] = (0.30, 0.50),
            ["grok-2"] = (2.00, 10.00),

            // Groq
            ["llama-3.3-70b-versatile"] = (0.59, 0.79),
            ["llama-3.1-8b-instant"] = (0.05, 0.08),
            ["mixtral-8x7b-32768"] = (0.24, 0.24),
            ["gemma2-9b-it"] = (0.20, 0.20),

            // Fireworks
            ["accounts/fireworks/models/llama-v3p3-70b-instruct"] = (0.90, 0.90),
            ["accounts/fireworks/models/llama-v3p1-8b-instruct"] = (0.20, 0.20),

            // Cerebras
            ["llama-3.3-70b"] = (0.85, 1.20),
            ["llama3.1-8b"] = (0.10, 0.10),

            // Zhipu AI / GLM
            ["glm-4.7"] = (0.60, 2.20),
            ["glm-4.5"] = (0.55, 2.00),
            ["glm-4.5-x"] = (2.20, 8.90),
            ["glm-4.5-air"] = (0.20, 1.10),
            ["glm-4.6"] = (0.60, 2.20),
            ["glm-4-32b-0414-128k"] = (0.10, 0.10),
            ["glm-4-plus"] = (0.60, 2.20),
            ["glm-4"] = (0.40, 1.60),
            ["glm-4-air"] = (0.20, 1.10),
            ["glm-4-long"] = (0.07, 0.07),

            // DashScope / Qwen (international pricing)
            ["qwen-max"] = (1.60, 6.40),
            ["qwen-plus"] = (0.40, 1.20),
            ["qwen-turbo"] = (0.05, 0.20),
            ["qwen-long"] = (0.07, 0.29),
            ["qwen2.5-72b-instruct"] = (0.175, 0.70),
            ["qwen2.5-7b-instruct"] = (0.175, 0.70),

            // Moonshot AI / Kimi
            ["moonshot-v1-8k"] = (0.20, 2.00),
            ["moonshot-v1-32k"] = (1.00, 3.00),
            ["moonshot-v1-128k"] = (2.00, 5.00),
            ["kimi-k2-0711-preview"] = (0.60, 2.50),
            ["kimi-k2-0905-preview"] = (0.60, 2.50),
            ["kimi-k2.5"] = (0.60, 3.00),
            ["kimi-k2-thinking"] = (0.60, 2.50),

            // MiniMax
            ["MiniMax-Text-01"] = (0.20, 1.10),
            ["MiniMax-M2"] = (0.255, 1.00),
            ["MiniMax-M2.1"] = (0.27, 0.95),
            ["MiniMax-M2.5"] = (0.295, 1.20),

            // VolcEngine / ByteDance Doubao
            ["doubao-1-5-pro-32k-250115"] = (0.11, 0.28),
            ["doubao-1-5-pro-256k-250115"] = (0.69, 1.24),
            ["doubao-1-5-lite-32k-250115"] = (0.04, 0.08),
            ["doubao-1.5-pro-32k"] = (0.11, 0.28),
            ["doubao-1.5-pro-256k"] = (0.69, 1.24),
            ["doubao-1.5-lite-32k"] = (0.04, 0.08),

            // SiliconFlow (hosted models — slash format)
            ["deepseek-ai/DeepSeek-V3"] = (0.27, 1.10),
            ["deepseek-ai/DeepSeek-R1"] = (0.55, 2.19),
            ["Qwen/Qwen2.5-72B-Instruct"] = (0.40, 1.20),
            ["Qwen/Qwen2.5-7B-Instruct"] = (0.05, 0.20),
            ["moonshotai/Kimi-K2-Instruct"] = (0.58, 2.29),
            ["MiniMaxAI/MiniMax-M2.5"] = (0.30, 1.20),
            ["meta-llama/Meta-Llama-3.1-8B-Instruct"] = (0.06, 0.06),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get pricing for a model. Returns (0, 0) for unknown models.
    /// </summary>
    public static (double InputPer1M, double OutputPer1M) GetPrice(string model) =>
        Prices.GetValueOrDefault(model);

    /// <summary>
    /// Calculate the USD cost for a given model and token counts.
    /// Returns 0.0 for unknown models.
    /// </summary>
    public static double CalculateCost(string model, int inputTokens, int outputTokens)
    {
        var (inputPer1M, outputPer1M) = GetPrice(model);
        if (inputPer1M == 0 && outputPer1M == 0)
        {
            return 0.0;
        }

        return (inputTokens * inputPer1M / 1_000_000.0)
               + (outputTokens * outputPer1M / 1_000_000.0);
    }

    /// <summary>
    /// Calculate cost using optional custom pricing overrides.
    /// Falls back to the hardcoded pricing table if no override is found.
    /// </summary>
    public static double CalculateCost(
        string model,
        int inputTokens,
        int outputTokens,
        IReadOnlyDictionary<string, ModelPricing>? overrides)
    {
        if (overrides is not null &&
            overrides.TryGetValue(model, out var custom))
        {
            return (inputTokens * custom.Input / 1_000_000.0)
                   + (outputTokens * custom.Output / 1_000_000.0);
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
    public static (double Cost, double Savings) CalculateCostWithCaching(
        string model,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheWriteTokens,
        IReadOnlyDictionary<string, ModelPricing>? overrides = null)
    {
        double inputPer1M, outputPer1M;

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
                return (0.0, 0.0);
            }
        }

        var outputCost = outputTokens * outputPer1M / 1_000_000.0;

        double inputCost, savings;

        if (IsAnthropicModel(model))
        {
            // inputTokens = regular uncached input (Anthropic's usage.input_tokens).
            // Cache writes are billed at 1.25× (premium for populating the cache).
            // Cache reads are billed at 0.10× (deep discount for cache hits).
            inputCost = (inputTokens * inputPer1M
                         + cacheWriteTokens * inputPer1M * 1.25
                         + cacheReadTokens * inputPer1M * 0.10)
                        / 1_000_000.0;

            // Net savings = discount on reads minus premium on writes.
            savings = (cacheReadTokens * inputPer1M * 0.90
                       - cacheWriteTokens * inputPer1M * 0.25)
                      / 1_000_000.0;
        }
        else
        {
            // OpenAI (and compatible): inputTokens = total prompt tokens INCLUDING cached.
            // Cached portion is re-priced at 0.50×; the rest stays at 1×.
            var regularInput = Math.Max(0, inputTokens - cacheReadTokens);
            inputCost = (regularInput * inputPer1M + cacheReadTokens * inputPer1M * 0.50)
                        / 1_000_000.0;

            // Savings = 50% discount on the cached tokens.
            savings = cacheReadTokens * inputPer1M * 0.50 / 1_000_000.0;
        }

        return (inputCost + outputCost, savings);
    }

    private static bool IsAnthropicModel(string model) =>
        model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase);
}