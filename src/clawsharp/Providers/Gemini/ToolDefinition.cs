using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Tool definition in the Gemini format (wraps function declarations).</summary>
internal sealed class ToolDefinition
{
    /// <summary>List of function declarations available to the model.</summary>
    [JsonPropertyName("functionDeclarations")]
    public List<FunctionDeclaration> FunctionDeclarations { get; init; } = [];
}