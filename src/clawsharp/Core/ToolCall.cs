namespace Clawsharp.Core;

/// <summary>A tool call requested by an LLM provider.</summary>
public sealed record ToolCall(
    /// <summary>Unique identifier for this tool call.</summary>
    string Id,
    /// <summary>The tool name to invoke.</summary>
    string Name,
    /// <summary>JSON-encoded arguments for the tool.</summary>
    string ArgumentsJson
);