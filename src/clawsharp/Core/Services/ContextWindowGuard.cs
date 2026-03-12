using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Clawsharp.Core.Services;

/// <summary>
///     Resolves context window sizes for LLM models and provides token estimation.
///     Uses a 3-tier lookup: config override, model ID lookup (with suffix stripping), provider fallback.
/// </summary>
public static partial class ContextWindowGuard
{
    /// <summary>Global default when no model or provider match is found.</summary>
    private const int GlobalDefault = 200_000;

    /// <summary>Exact model ID to context window mapping.</summary>
    private static readonly FrozenDictionary<string, int> ModelWindows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        // Anthropic Claude
        ["claude-opus-4-6"] = 200_000,
        ["claude-opus-4.6"] = 200_000,
        ["claude-sonnet-4-6"] = 200_000,
        ["claude-sonnet-4.6"] = 200_000,
        ["claude-haiku-4-5"] = 200_000,
        ["claude-3-5-sonnet"] = 200_000,
        ["claude-3-5-haiku"] = 200_000,
        ["claude-3-opus"] = 200_000,

        // OpenAI GPT
        ["gpt-5.2"] = 128_000,
        ["gpt-5.2-codex"] = 128_000,
        ["gpt-4.5-preview"] = 128_000,
        ["gpt-4.1"] = 128_000,
        ["gpt-4.1-mini"] = 128_000,
        ["gpt-4o"] = 128_000,
        ["gpt-4o-mini"] = 128_000,
        ["gpt-4-turbo"] = 128_000,
        ["gpt-4"] = 8_192,
        ["gpt-3.5-turbo"] = 16_385,

        // OpenAI o1 / o3 family
        ["o1"] = 200_000,
        ["o1-mini"] = 128_000,
        ["o1-preview"] = 128_000,
        ["o1-pro"] = 200_000,
        ["o3"] = 200_000,
        ["o3-mini"] = 128_000,
        ["o3-pro"] = 200_000,
        ["o4-mini"] = 200_000,

        // Google Gemini
        ["gemini-2.5-pro"] = 1_048_576,
        ["gemini-2.5-flash"] = 1_048_576,
        ["gemini-2.0-flash"] = 1_048_576,
        ["gemini-1.5-pro"] = 2_097_152,
        ["gemini-1.5-flash"] = 1_048_576,

        // DeepSeek
        ["deepseek-v3.2"] = 128_000,
        ["deepseek-chat"] = 128_000,
        ["deepseek-reasoner"] = 128_000,
        ["deepseek-v3"] = 128_000,
        ["deepseek-v2"] = 128_000,

        // Meta Llama 4
        ["llama-4-70b-instruct"] = 128_000,
        ["llama-4-scout"] = 10_000_000,

        // Meta Llama 3.3
        ["llama-3.3-70b-versatile"] = 128_000,
        ["llama-3.3-70b-instruct"] = 128_000,

        // Meta Llama 3.2
        ["llama-3.2-90b-vision-instruct"] = 128_000,
        ["llama-3.2-11b-vision-instruct"] = 128_000,
        ["llama-3.2-3b-instruct"] = 128_000,
        ["llama-3.2-1b-instruct"] = 128_000,

        // Meta Llama 3.1
        ["llama-3.1-405b-instruct"] = 128_000,
        ["llama-3.1-70b-instruct"] = 128_000,
        ["llama-3.1-8b-instruct"] = 128_000,
        ["llama-3.1-8b-instant"] = 128_000,

        // Mistral / Mixtral
        ["mistral-large-latest"] = 128_000,
        ["mistral-medium-latest"] = 128_000,
        ["mistral-small-latest"] = 128_000,
        ["mistral-nemo"] = 128_000,
        ["mistral-7b-instruct"] = 32_768,
        ["codestral-latest"] = 32_768,
        ["mixtral-8x7b-32768"] = 32_768,
        ["mixtral-8x22b-instruct"] = 65_536,

        // Cohere Command
        ["command-r-plus"] = 128_000,
        ["command-r"] = 128_000,
        ["command-a-03-2025"] = 256_000,
        ["command-r7b-12-2024"] = 128_000,

        // Microsoft Phi
        ["phi-4"] = 16_384,
        ["phi-3.5-mini-instruct"] = 128_000,
        ["phi-3-medium-128k-instruct"] = 128_000,
        ["phi-3-small-128k-instruct"] = 128_000,
        ["phi-3-mini-128k-instruct"] = 128_000,
        ["phi-3-mini-4k-instruct"] = 4_096,

        // Qwen (Alibaba)
        ["qwen2.5-72b-instruct"] = 131_072,
        ["qwen2.5-7b-instruct"] = 131_072,
        ["qwen-max"] = 32_768,
        ["qwen-turbo"] = 131_072,
        ["qwen-plus"] = 131_072,
        ["qwen-long"] = 10_000_000,

        // Zhipu GLM
        ["glm-4.7"] = 200_000,
        ["glm-4.5"] = 131_072,
        ["glm-4.5-x"] = 131_072,
        ["glm-4.5-air"] = 131_072,
        ["glm-4.6"] = 131_072,
        ["glm-4-32b-0414-128k"] = 131_072,
        ["glm-4-plus"] = 131_072,
        ["glm-4"] = 131_072,
        ["glm-4-air"] = 131_072,
        ["glm-4-long"] = 1_000_000,

        // Moonshot Kimi K2
        ["kimi-k2-0711-preview"] = 131_072,
        ["kimi-k2-0905-preview"] = 262_144,
        ["kimi-k2.5"] = 131_072,
        ["kimi-k2-thinking"] = 131_072,

        // VolcEngine Doubao
        ["doubao-1-5-pro-32k-250115"] = 32_768,
        ["doubao-1-5-pro-256k-250115"] = 262_144,
        ["doubao-1-5-lite-32k-250115"] = 32_768,
        ["doubao-1.5-pro-32k"] = 32_768,
        ["doubao-1.5-pro-256k"] = 262_144,
        ["doubao-1.5-lite-32k"] = 32_768,

        // MiniMax (specific models)
        ["MiniMax-Text-01"] = 1_000_000,
        ["MiniMax-M2"] = 204_800,
        ["MiniMax-M2.1"] = 204_800,
        ["MiniMax-M2.5"] = 204_800,

        // Google Gemma
        ["gemma-2-27b-it"] = 8_192,
        ["gemma-2-9b-it"] = 8_192,
        ["gemma-3-27b-it"] = 131_072,
        ["gemma-3-12b-it"] = 131_072,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Provider-level fallback windows (used when model ID doesn't match).</summary>
    private static readonly FrozenDictionary<string, int> ProviderWindows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["openrouter"] = 200_000,
        ["minimax"] = 200_000,
        ["openai-codex"] = 200_000,
        ["moonshot"] = 256_000,
        ["kimi"] = 262_144,
        ["kimi-coding"] = 262_144,
        ["xiaomi"] = 262_144,
        ["ollama"] = 128_000,
        ["qwen"] = 131_072,
        ["vllm"] = 128_000,
        ["github-copilot"] = 128_000,
        ["qianfan"] = 98_304,
        ["nvidia"] = 131_072,
        ["cohere"] = 128_000,
        ["mistral"] = 128_000,
        ["perplexity"] = 127_072,
        ["together"] = 128_000,
        ["fireworks"] = 128_000,
        ["groq"] = 32_768,
        ["cerebras"] = 128_000,
        ["anthropic"] = 200_000,
        ["google"] = 1_048_576,
        ["bedrock"] = 200_000,
        ["zhipu"] = 131_072,
        ["dashscope"] = 131_072,
        ["volcengine"] = 32_768,
        ["siliconflow"] = 131_072,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Resolves the context window size (in tokens) for a given model ID.
    ///     Uses a 3-tier strategy: config override, model lookup (with suffix stripping
    ///     and nested provider resolution), then pattern-based inference.
    /// </summary>
    /// <param name="modelId">The model identifier (e.g. "gpt-4o", "openrouter/anthropic/claude-sonnet-4.6").</param>
    /// <param name="configOverride">Optional user-configured context window size.</param>
    /// <returns>The resolved context window size in tokens.</returns>
    public static int ResolveContextWindow(string modelId, int? configOverride = null)
    {
        // Tier 0: config override takes priority
        if (configOverride is > 0)
        {
            return configOverride.Value;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            return GlobalDefault;
        }

        // Tier 1: exact model ID match
        if (ModelWindows.TryGetValue(modelId, out var window))
        {
            return window;
        }

        // Tier 1b: strip "-latest" suffix
        if (modelId.EndsWith("-latest", StringComparison.OrdinalIgnoreCase))
        {
            var stripped = modelId[..^7]; // "-latest".Length == 7
            if (ModelWindows.TryGetValue(stripped, out window))
            {
                return window;
            }
        }

        // Tier 1c: strip date suffix (YYYYMMDD after last dash)
        var dateSuffixMatch = DateSuffixRegex().Match(modelId);
        if (dateSuffixMatch.Success)
        {
            var stripped = modelId[..dateSuffixMatch.Index];
            if (ModelWindows.TryGetValue(stripped, out window))
            {
                return window;
            }
        }

        // Tier 2: nested provider ref (e.g. "openrouter/anthropic/claude-sonnet-4.6")
        if (modelId.Contains('/'))
        {
            var segments = modelId.Split('/');

            // Try provider fallback from first segment
            if (ProviderWindows.TryGetValue(segments[0], out window))
            {
                return window;
            }

            // Try model part (everything after last /)
            var leafModel = segments[^1];
            if (ModelWindows.TryGetValue(leafModel, out window))
            {
                return window;
            }

            // Try pattern inference on leaf model
            var leafPattern = InferFromPattern(leafModel);
            if (leafPattern > 0)
            {
                return leafPattern;
            }
        }

        // Tier 3: pattern inference on full model ID
        var patternResult = InferFromPattern(modelId);
        if (patternResult > 0)
        {
            return patternResult;
        }

        // Tier 4: global default
        return GlobalDefault;
    }

    /// <summary>
    ///     Estimates the token count for a string using a simple heuristic (1 token per ~4 characters).
    ///     This is intentionally conservative — real tokenizers produce fewer tokens for English text.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (text.Length + 3) / 4;
    }

    /// <summary>
    ///     Estimates the total token count for a list of chat messages, including per-message overhead
    ///     and tool call content.
    /// </summary>
    public static int EstimateTokens(IEnumerable<ChatMessage> messages)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            total += 4; // per-message overhead (role, separators)
            total += EstimateTokens(msg.Content ?? "");

            if (msg.ToolCalls is { Count: > 0 })
            {
                foreach (var tc in msg.ToolCalls)
                {
                    total += EstimateTokens(tc.ArgumentsJson) + EstimateTokens(tc.Name) + 4;
                }
            }
        }

        return total;
    }

    /// <summary>Whether the estimated token count exceeds the compaction trigger threshold.</summary>
    public static bool ShouldCompact(int estimatedTokens, int contextWindow, double triggerPercent = 0.75)
        => estimatedTokens > (int)(contextWindow * triggerPercent);

    /// <summary>Whether the estimated token count is at emergency levels requiring immediate force-compression.</summary>
    public static bool IsEmergency(int estimatedTokens, int contextWindow, double emergencyPercent = 0.90)
        => estimatedTokens > (int)(contextWindow * emergencyPercent);

    /// <summary>Infer context window from model ID prefix patterns. Returns 0 if no pattern matches.</summary>
    private static int InferFromPattern(string modelId)
    {
        if (modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
        {
            return 200_000;
        }

        if (modelId.StartsWith("gpt", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("o4", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
        {
            return 1_048_576;
        }

        if (modelId.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
        {
            return 8_192;
        }

        if (modelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("llama", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("phi", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("command", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("mistral", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("mixtral", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("codestral", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
        {
            return 131_072;
        }

        if (modelId.StartsWith("glm", StringComparison.OrdinalIgnoreCase))
        {
            return 131_072;
        }

        if (modelId.StartsWith("moonshot", StringComparison.OrdinalIgnoreCase))
        {
            return 128_000;
        }

        if (modelId.StartsWith("kimi", StringComparison.OrdinalIgnoreCase))
        {
            return 131_072;
        }

        if (modelId.StartsWith("doubao", StringComparison.OrdinalIgnoreCase))
        {
            return 32_768;
        }

        if (modelId.StartsWith("minimax", StringComparison.OrdinalIgnoreCase))
        {
            return 204_800;
        }

        if (modelId.StartsWith("falcon", StringComparison.OrdinalIgnoreCase))
        {
            return 32_768;
        }

        return 0;
    }

    /// <summary>Matches a date suffix like "-20260304" at the end of a model ID.</summary>
    [GeneratedRegex(@"-\d{8}$")]
    private static partial Regex DateSuffixRegex();
}