using Intellenum;

namespace Clawsharp.Config.Features;

/// <summary>Well-known tool filter group modes.</summary>
[Intellenum<string>]
internal partial class ToolFilterMode
{
    /// <summary>Always include matching tools in LLM requests.</summary>
    public static readonly ToolFilterMode Always = new("always");

    /// <summary>Include matching tools only when message text contains a keyword.</summary>
    public static readonly ToolFilterMode Dynamic = new("dynamic");
}
