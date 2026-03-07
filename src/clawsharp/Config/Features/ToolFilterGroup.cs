namespace Clawsharp.Config.Features;

/// <summary>
/// Defines a filter group for dynamic tool inclusion.
/// Tools matching the group's patterns are included based on the mode:
/// "always" (default) includes them unconditionally; "dynamic" includes
/// them only when the user message contains one of the specified keywords.
/// </summary>
public sealed class ToolFilterGroup
{
    /// <summary>Filter mode: "always" or "dynamic".</summary>
    public string Mode { get; init; } = "always";

    /// <summary>
    /// Glob patterns matching tool names (e.g. "mcp__brave*", "web_*").
    /// Uses <c>*</c> for any sequence and <c>?</c> for a single character.
    /// </summary>
    public List<string>? ToolPatterns { get; init; }

    /// <summary>
    /// Keywords that trigger inclusion when <see cref="Mode"/> is "dynamic".
    /// Matching is case-insensitive and checks for substring containment.
    /// </summary>
    public List<string>? Keywords { get; init; }
}