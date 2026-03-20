using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// Provider routing preferences for OpenRouter. Controls which upstream providers
/// serve a request and how fallback behavior works.
/// </summary>
internal sealed class OpenRouterProviderPreferences
{
    /// <summary>
    /// Ordered list of preferred upstream providers (e.g. "Anthropic", "Google").
    /// OpenRouter will try providers in this order.
    /// </summary>
    [JsonPropertyName("order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Order { get; init; }

    /// <summary>Whether to allow fallback to providers not in <see cref="Order"/>.</summary>
    [JsonPropertyName("allow_fallbacks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowFallbacks { get; init; }

    /// <summary>Require that the chosen provider supports all parameters in the request.</summary>
    [JsonPropertyName("require_parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireParameters { get; init; }

    /// <summary>
    /// Zero Data Retention. When true, only routes to endpoints that will not store
    /// or train on your data. Operates as OR with account-level ZDR setting
    /// (can only enforce, not override account-wide ZDR).
    /// </summary>
    [JsonPropertyName("zdr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ZeroDataRetention { get; init; }

    /// <summary>
    /// Data collection policy. Set to "deny" to prevent providers from collecting
    /// your data for training or improvement purposes.
    /// </summary>
    [JsonPropertyName("data_collection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataCollection { get; init; }
}
