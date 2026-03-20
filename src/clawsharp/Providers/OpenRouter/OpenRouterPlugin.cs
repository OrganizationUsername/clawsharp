using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// An OpenRouter plugin activation (e.g. web search, response healing).
/// </summary>
internal sealed class OpenRouterPlugin
{
    /// <summary>Plugin identifier (e.g. "web-search").</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Whether the plugin is enabled. Null omits the field (uses server default).</summary>
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; init; }
}
