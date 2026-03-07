using System.Text.Json.Serialization;

namespace Clawsharp.Config.Agent;

/// <summary>Configuration for an LLM provider.</summary>
public sealed class ProviderConfig
{
    /// <summary>Provider type ("openai", "anthropic", "gemini", "ollama", "lmstudio").</summary>
    public string Type { get; set; } = "openai";

    /// <summary>Parsed <see cref="LlmProviderType"/> value derived from <see cref="Type"/>.</summary>
    [JsonIgnore]
    public LlmProviderType ProviderType
    {
        get => LlmProviderType.TryFromValue(Type, out var t) ? t : LlmProviderType.OpenAi;
        // No-op setter: required for the Configuration Binding Source Generator (EnableConfigurationBindingGenerator).
        // The generator emits assignment code for all public settable properties; value is ignored at runtime
        // because ProviderType is always derived from the Type string property.
        internal set { }
    }

    /// <summary>API key for the provider.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Custom base URL for the provider API.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>AWS Access Key ID (Bedrock only).</summary>
    public string? AwsAccessKeyId { get; set; }

    /// <summary>AWS Secret Access Key (Bedrock only).</summary>
    public string? AwsSecretAccessKey { get; set; }

    /// <summary>AWS region (Bedrock only, default "us-east-1").</summary>
    public string? AwsRegion { get; set; }

    /// <summary>
    /// Custom HTTP header name for API key authentication.
    /// When set, the API key is sent as <c>{AuthHeader}: {ApiKey}</c> instead of
    /// the default <c>Authorization: Bearer {ApiKey}</c>.
    /// Useful for Azure OpenAI (<c>"api-key"</c>) and similar non-standard auth schemes.
    /// </summary>
    public string? AuthHeader { get; set; }
}