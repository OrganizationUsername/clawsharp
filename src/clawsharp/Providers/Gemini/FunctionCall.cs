using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>A function call made by the Gemini model.</summary>
internal sealed class FunctionCall
{
    /// <summary>The function name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Arguments as a JSON object.</summary>
    [JsonPropertyName("args")]
    public JsonElement Args { get; init; }
}