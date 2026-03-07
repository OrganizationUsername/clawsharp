using System.Text.Json.Serialization;

namespace Clawsharp.Cli.Models;

/// <summary>Response from the Gemini /models endpoint.</summary>
internal sealed class GeminiModelsResponse
{
    [JsonPropertyName("models")]
    public List<GeminiModelItem>? Models { get; set; }
}

/// <summary>A single model entry from the Gemini API.</summary>
internal sealed class GeminiModelItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}