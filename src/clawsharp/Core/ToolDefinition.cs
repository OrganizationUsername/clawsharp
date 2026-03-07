namespace Clawsharp.Core;

/// <summary>Definition of a tool available for LLM function calling.</summary>
public sealed record ToolDefinition(
    /// <summary>The tool name.</summary>
    string Name,
    /// <summary>Description of what the tool does.</summary>
    string Description,
    /// <summary>JSON Schema describing the tool's input parameters.</summary>
    string ParametersSchemaJson
);