using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>Response from GET /api/v1/models.</summary>
internal sealed class OpenRouterModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenRouterModel> Data { get; init; } = [];
}

/// <summary>An AI model available on OpenRouter.</summary>
internal sealed class OpenRouterModel
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("context_length")]
    public int? ContextLength { get; init; }

    [JsonPropertyName("pricing")]
    public OpenRouterModelPricing? Pricing { get; init; }

    [JsonPropertyName("architecture")]
    public OpenRouterModelArchitecture? Architecture { get; init; }

    [JsonPropertyName("top_provider")]
    public OpenRouterTopProvider? TopProvider { get; init; }
}

/// <summary>Pricing per token in USD (string representation of decimal).</summary>
internal sealed class OpenRouterModelPricing
{
    /// <summary>Price per prompt token in USD (string like "0.000003").</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>Price per completion token in USD (string like "0.000015").</summary>
    [JsonPropertyName("completion")]
    public string? Completion { get; init; }
}

/// <summary>Model architecture info.</summary>
internal sealed class OpenRouterModelArchitecture
{
    /// <summary>Supported input modalities (e.g. "text", "image").</summary>
    [JsonPropertyName("input_modalities")]
    public List<string>? InputModalities { get; init; }

    /// <summary>Supported output modalities (e.g. "text", "image").</summary>
    [JsonPropertyName("output_modalities")]
    public List<string>? OutputModalities { get; init; }
}

/// <summary>Top provider info for a model.</summary>
internal sealed class OpenRouterTopProvider
{
    /// <summary>Maximum context window size in tokens for the top provider.</summary>
    [JsonPropertyName("context_length")]
    public int? ContextLength { get; init; }

    /// <summary>Maximum number of completion tokens the top provider supports.</summary>
    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; init; }

    /// <summary>Whether the top provider applies content moderation.</summary>
    [JsonPropertyName("is_moderated")]
    public bool IsModerated { get; init; }
}
