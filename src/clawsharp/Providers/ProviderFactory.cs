using Clawsharp.Auth;
using Clawsharp.Core.Utilities;
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
    private static IProvider Create(ProviderCreationOptions options)
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

        var extraHeaders = config.ExtraHeaders;
        var apiKeysList = BuildApiKeysList(apiKey, config.ApiKeys);

        if (providerType == LlmProviderType.Anthropic)
        {
            return new AnthropicProvider(httpClientFactory, apiKey, providerName, extraHeaders, apiKeysList);
        }

        if (providerType == LlmProviderType.Gemini)
        {
            return new GeminiProvider(httpClientFactory, apiKey, providerName);
        }

        if (providerType == LlmProviderType.Copilot)
        {
            return new CopilotProvider(httpClientFactory,
                options.DeviceFlow ??
                throw new InvalidOperationException(
                    "GitHubDeviceFlow is required for the Copilot provider. Ensure it is registered in DI."));
        }

        if (providerType == LlmProviderType.Ollama)
        {
            return new OllamaProvider(httpClientFactory, baseUrl ?? ClawsharpConstants.OllamaDefaultBaseUrl);
        }

        if (providerType == LlmProviderType.LmStudio)
        {
            return new LmStudioProvider(httpClientFactory, baseUrl ?? ClawsharpConstants.LmStudioDefaultBaseUrl);
        }

        if (providerType == LlmProviderType.Bedrock)
        {
            return new BedrockProvider(
                httpClientFactory,
                config.AwsAccessKeyId ?? "",
                config.AwsSecretAccessKey ?? "",
                config.AwsRegion ?? "us-east-1",
                providerName);
        }

        // All remaining types are OpenAI-compatible with provider-specific default base URLs
        if (ClawsharpConstants.DefaultProviderBaseUrls.TryGetValue(providerType, out var defaultBaseUrl))
        {
            return new OpenAiProvider(httpClientFactory, baseUrl ?? defaultBaseUrl, apiKey, providerName, authHeader, extraHeaders, apiKeysList);
        }

        if (providerType == LlmProviderType.VertexAi)
        {
            throw new ArgumentException(
                "Vertex AI requires an explicit baseUrl in provider config " +
                "(e.g. \"https://{region}-aiplatform.googleapis.com/v1/projects/{project}/locations/{region}/publishers/google/models\"). " +
                "There is no sensible default because the URL includes your GCP project and region.");
        }

        throw new InvalidOperationException($"No default base URL for provider type '{providerType.Value}'.");

    }

    /// <summary>
    ///     Builds a consolidated API keys list for round-robin rotation.
    ///     If <paramref name="additionalKeys"/> is null or empty, returns null (single-key mode).
    ///     Otherwise, prepends the primary <paramref name="primaryKey"/> and returns the full list.
    /// </summary>
    private static List<string>? BuildApiKeysList(string primaryKey, List<string>? additionalKeys)
    {
        if (additionalKeys is not { Count: > 0 })
        {
            return null;
        }

        var allKeys = new List<string>(additionalKeys.Count + 1);
        if (!string.IsNullOrEmpty(primaryKey))
        {
            allKeys.Add(primaryKey);
        }

        allKeys.AddRange(additionalKeys);
        return allKeys;
    }
}