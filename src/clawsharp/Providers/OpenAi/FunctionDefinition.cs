using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Function definition describing a callable tool for the OpenAI API.</summary>
internal sealed class FunctionDefinition
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