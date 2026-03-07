using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A tool call made by the assistant in an OpenAI chat completion response.</summary>
internal sealed class FunctionToolCall
{
    /// <summary>Unique identifier for this tool call.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Tool type (always "function").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    /// <summary>The function call details (name and arguments).</summary>
    [JsonPropertyName("function")]
    public FunctionToolCallDetail Function { get; init; } = new();
}