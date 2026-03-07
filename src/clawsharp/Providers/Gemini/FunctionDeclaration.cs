using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>A function declaration describing a callable tool for the Gemini API.</summary>
internal sealed class FunctionDeclaration
{
    /// <summary>The function name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Description of what the function does.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>JSON Schema describing the function's parameters.</summary>
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}