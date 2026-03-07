using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>
/// Marks an Anthropic content block or tool definition as eligible for prompt caching.
/// Serializes to <c>{"type":"ephemeral"}</c>.
/// </summary>
internal sealed class CacheControl
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "ephemeral";
}