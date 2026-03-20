using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// Response from the OpenRouter Chat Completions API.
/// Extends the standard OpenAI response with <see cref="OpenRouterUsage.Cost"/> and
/// <see cref="OpenRouterChoice.NativeFinishReason"/>.
/// </summary>
internal sealed class OpenRouterResponse
{
    /// <summary>Unique request identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The model that actually served the request (may differ from the requested model).</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>List of completion choices (typically one).</summary>
    [JsonPropertyName("choices")]
    public List<OpenRouterChoice> Choices { get; init; } = [];

    /// <summary>Token usage statistics including cost.</summary>
    [JsonPropertyName("usage")]
    public OpenRouterUsage? Usage { get; init; }
}
