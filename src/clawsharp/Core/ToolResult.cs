namespace Clawsharp.Core;

/// <summary>The result of executing a tool call.</summary>
public sealed record ToolResult(
    /// <summary>ID of the tool call this result corresponds to.</summary>
    string ToolCallId,
    /// <summary>Output content from the tool execution.</summary>
    string Content,
    /// <summary>Whether the tool execution resulted in an error.</summary>
    bool IsError = false
);