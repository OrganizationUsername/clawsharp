using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>A function response returning tool output to the Gemini API.</summary>
internal sealed class FunctionResponse
{
    /// <summary>The function name this response corresponds to.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The function response data as a JSON object.</summary>
    [JsonPropertyName("response")]
    public JsonElement Response { get; init; }
}