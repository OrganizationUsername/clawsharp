using Intellenum;

namespace Clawsharp.Core;

/// <summary>Canonical finish reason returned by LLM providers after normalization.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class FinishReason
{
    /// <summary>Normal completion (model stopped generating).</summary>
    public static readonly FinishReason Stop = new("stop");

    /// <summary>Model hit the maximum token limit.</summary>
    public static readonly FinishReason Length = new("length");

    /// <summary>Model requested one or more tool calls.</summary>
    public static readonly FinishReason ToolCalls = new("tool_calls");

    /// <summary>Response blocked by a safety or content filter.</summary>
    public static readonly FinishReason ContentFilter = new("content_filter");
}