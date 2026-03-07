using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Generation configuration for the Gemini API (temperature, max tokens).</summary>
internal sealed class GenerationConfig
{
    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Maximum number of output tokens.</summary>
    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; init; }
}