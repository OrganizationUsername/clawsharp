using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Details of a function call within a tool call (name and JSON arguments).</summary>
internal sealed class FunctionToolCallDetail
{
    /// <summary>The name of the function to call.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>JSON-encoded arguments for the function.</summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "";
}