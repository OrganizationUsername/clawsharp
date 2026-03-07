using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Tool definition in the OpenAI function calling format.</summary>
internal sealed class FunctionTool
{
    /// <summary>Tool type (always "function").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    /// <summary>The function definition.</summary>
    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; init; } = new();
}