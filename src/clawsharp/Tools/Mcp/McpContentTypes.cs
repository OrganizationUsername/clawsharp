using Intellenum;

namespace Clawsharp.Tools.Mcp;

/// <summary>Well-known MCP content types.</summary>
[Intellenum<string>]
internal partial class McpContentType
{
    public static readonly McpContentType Text = new("text");
}
