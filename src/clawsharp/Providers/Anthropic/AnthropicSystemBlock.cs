using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>
/// A single block in the Anthropic <c>system</c> array.
/// When <see cref="CacheControl"/> is set, everything up to and including this block
/// is stored as a single cache entry (up to 4 breakpoints per request).
/// </summary>
internal sealed class AnthropicSystemBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CacheControl? CacheControl { get; init; }
}