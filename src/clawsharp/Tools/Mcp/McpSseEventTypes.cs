using Intellenum;

namespace Clawsharp.Tools.Mcp;

/// <summary>Well-known MCP Server-Sent Event types.</summary>
[Intellenum<string>]
internal partial class McpSseEventType
{
    public static readonly McpSseEventType Endpoint = new("endpoint");
    public static readonly McpSseEventType Message = new("message");
}
