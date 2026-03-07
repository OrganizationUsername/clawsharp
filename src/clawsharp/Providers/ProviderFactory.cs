using Clawsharp.Auth;
using Clawsharp.Config;
using Clawsharp.Providers.Anthropic;
using Clawsharp.Providers.Bedrock;
using Clawsharp.Providers.Copilot;
using Clawsharp.Providers.Gemini;
using Clawsharp.Providers.LmStudio;
using Clawsharp.Providers.Ollama;
using Clawsharp.Providers.OpenAi;
using Clawsharp.Config.Agent;

namespace Clawsharp.Providers;

/// <summary>
/// Encapsulates all parameters needed to create a provider instance,
/// including optional per-fallback overrides for API key, base URL, and auth header.
/// </summary>
public sealed record ProviderCreationOptions(
    string ProviderName,
    Dictionary<string, ProviderConfig> Configs,
    IHttpClientFactory HttpClientFactory,
    GitHubDeviceFlow? DeviceFlow = null,
    string? ApiKeyOverride = null,
    string? BaseUrlOverride = null,
    string? AuthHeaderOverride = null);

public static class ProviderFactory
{
    /// <summary>
    ///     Creates a provider instance from the named provider configuration.
    /// </summary>
    public static IProvider Create(string providerName, Dictionary<string, ProviderConfig> configs, IHttpClientFactory httpClientFactory,
                                   GitHubDeviceFlow? deviceFlow = null)
        => Create(new ProviderCreationOptions(providerName, configs, httpClientFactory, deviceFlow));

    /// <summary>
    ///     Creates a provider instance with optional per-fallback overrides for API key, base URL, and auth header.
    ///     When an override is non-null, it replaces the value from the provider's config entry.
    /// </summary>
    public static IProvider Create(
        string providerName,
        Dictionary<string, ProviderConfig> configs,
        IHttpClientFactory httpClientFactory,
        GitHubDeviceFlow? deviceFlow,
        string? apiKeyOverride,
        string? baseUrlOverride,
        string? authHeaderOverride)
        => Create(new ProviderCreationOptions(providerName, configs, httpClientFactory, deviceFlow, apiKeyOverride, baseUrlOverride,
            authHeaderOverride));

    /// <summary>
    ///     Creates a provider instance from a <see cref="ProviderCreationOptions"/> record.
    /// </summary>
    public static IProvider Create(ProviderCreationOptions options)
    {
        if (!options.Configs.TryGetValue(options.ProviderName, out var config))
        {
            throw new InvalidOperationException($"Provider '{options.ProviderName}' not found in config.");
        }

        var typeName = config.Type?.ToLowerInvariant() ?? "";
        if (!LlmProviderType.TryFromValue(typeName, out var providerType))
        {
            throw new InvalidOperationException($"Unknown provider type '{config.Type}'.");
        }

        // Apply per-fallback overrides (non-null values replace config values)
        var apiKey = options.ApiKeyOverride ?? config.ApiKey ?? "";
        var baseUrl = options.BaseUrlOverride ?? config.BaseUrl;
        var authHeader = options.AuthHeaderOverride ?? config.AuthHeader;
        var providerName = options.ProviderName;
        var httpClientFactory = options.HttpClientFactory;

        if (providerType == LlmProviderType.Anthropic)
            return new AnthropicProvider(httpClientFactory, apiKey, providerName);
        if (providerType == LlmProviderType.Gemini)
            return new GeminiProvider(httpClientFactory, apiKey, providerName);
        if (providerType == LlmProviderType.Copilot)
            return new CopilotProvider(httpClientFactory,
                options.DeviceFlow ??
                throw new InvalidOperationException(
                    "GitHubDeviceFlow is required for the Copilot provider. Ensure it is registered in DI."));
        if (providerType == LlmProviderType.Ollama)
            return new OllamaProvider(httpClientFactory, baseUrl ?? "http://localhost:11434");
        if (providerType == LlmProviderType.LmStudio)
            return new LmStudioProvider(httpClientFactory, baseUrl ?? "http://localhost:1234");
        if (providerType == LlmProviderType.Bedrock)
            return new BedrockProvider(
                httpClientFactory,
                config.AwsAccessKeyId ?? "",
                config.AwsSecretAccessKey ?? "",
                config.AwsRegion ?? "us-east-1",
                providerName);

        // All remaining types are OpenAI-compatible with provider-specific default base URLs
        var defaultBaseUrl = GetDefaultBaseUrl(providerType);
        return new OpenAiProvider(httpClientFactory, baseUrl ?? defaultBaseUrl, apiKey, providerName, authHeader);
    }

    /// <summary>Returns the default base URL for an OpenAI-compatible provider type.</summary>
    private static string GetDefaultBaseUrl(LlmProviderType providerType)
    {
        if (providerType == LlmProviderType.OpenAi) return "https://api.openai.com/v1";
        if (providerType == LlmProviderType.OpenRouter) return "https://openrouter.ai/api/v1";
        if (providerType == LlmProviderType.Groq) return "https://api.groq.com/openai/v1";
        if (providerType == LlmProviderType.DeepSeek) return "https://api.deepseek.com/v1";
        if (providerType == LlmProviderType.Mistral) return "https://api.mistral.ai/v1";
        if (providerType == LlmProviderType.Perplexity) return "https://api.perplexity.ai";
        if (providerType == LlmProviderType.XAi) return "https://api.x.ai/v1";
        if (providerType == LlmProviderType.VLlm) return "http://localhost:8000/v1";
        if (providerType == LlmProviderType.LlamaCpp) return "http://localhost:8080/v1";
        if (providerType == LlmProviderType.TogetherAi) return "https://api.together.xyz/v1";
        if (providerType == LlmProviderType.Fireworks) return "https://api.fireworks.ai/inference/v1";
        if (providerType == LlmProviderType.Cerebras) return "https://api.cerebras.ai/v1";
        if (providerType == LlmProviderType.Novita) return "https://api.novita.ai/v3/openai";
        if (providerType == LlmProviderType.HuggingFace) return "https://api-inference.huggingface.co/v1";
        if (providerType == LlmProviderType.DashScope) return "https://dashscope.aliyuncs.com/compatible-mode/v1";
        if (providerType == LlmProviderType.Zhipu) return "https://open.bigmodel.cn/api/paas/v4/";
        if (providerType == LlmProviderType.Moonshot) return "https://api.moonshot.cn/v1";
        if (providerType == LlmProviderType.Volcengine) return "https://ark.cn-beijing.volces.com/api/v3";
        if (providerType == LlmProviderType.Minimax) return "https://api.minimax.chat/v1";
        if (providerType == LlmProviderType.SiliconFlow) return "https://api.siliconflow.cn/v1";
        if (providerType == LlmProviderType.Cohere) return "https://api.cohere.com/compatibility/v1";
        if (providerType == LlmProviderType.SambaNova) return "https://api.sambanova.ai/v1";
        if (providerType == LlmProviderType.Ai21) return "https://api.ai21.com/studio/v1";
        if (providerType == LlmProviderType.Replicate) return "https://api.replicate.com/v1";
        if (providerType == LlmProviderType.VertexAi)
            throw new ArgumentException(
                "Vertex AI requires an explicit baseUrl in provider config " +
                "(e.g. \"https://{region}-aiplatform.googleapis.com/v1/projects/{project}/locations/{region}/publishers/google/models\"). " +
                "There is no sensible default because the URL includes your GCP project and region.");

        // Unreachable for known types — but covers future additions gracefully
        throw new InvalidOperationException($"No default base URL for provider type '{providerType.Value}'.");
    }
}