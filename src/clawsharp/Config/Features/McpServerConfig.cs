namespace Clawsharp.Config.Features;

/// <summary>
/// Configuration for a single MCP (Model Context Protocol) server child process.
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>
    /// Environment variable names that are denied for MCP server processes.
    /// These can be used for library injection attacks.
    /// </summary>
    private static readonly HashSet<string> DeniedEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "LD_PRELOAD",
        "LD_LIBRARY_PATH",
        "DYLD_INSERT_LIBRARIES",
        "DYLD_LIBRARY_PATH",
        "DYLD_FRAMEWORK_PATH",
    };

    /// <summary>
    /// Transport type: "stdio" (default), "sse", or "streamableHttp".
    /// When null, auto-detected: if <see cref="Url"/> is set and <see cref="Command"/> is empty,
    /// URLs ending in "/sse" use SSE transport, otherwise StreamableHTTP.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// URL for SSE or StreamableHTTP transports. Required when <see cref="Type"/> is "sse" or "streamableHttp".
    /// For SSE, this is the SSE endpoint URL. For StreamableHTTP, this is the JSON-RPC endpoint URL.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>Optional HTTP headers for SSE or StreamableHTTP transports (e.g. authorization).</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Command to launch the MCP server process (e.g. "npx", "python", "uvx"). Used for stdio transport.</summary>
    public string Command { get; init; } = "";

    /// <summary>Arguments for the command (stdio transport only).</summary>
    public List<string>? Args { get; init; }

    /// <summary>Environment variables to set for the process (stdio transport only).</summary>
    public Dictionary<string, string>? Env { get; init; }

    /// <summary>
    /// Resolves the effective transport type based on <see cref="Type"/>, <see cref="Url"/>,
    /// and <see cref="Command"/>. Returns "stdio", "sse", or "streamableHttp".
    /// </summary>
    public string ResolveTransportType()
    {
        // Explicit type takes precedence
        if (!string.IsNullOrEmpty(Type))
        {
            return Type;
        }

        // Auto-detect: if URL is set and Command is empty, infer HTTP transport
        if (!string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(Command))
        {
            return Url.EndsWith("/sse", StringComparison.OrdinalIgnoreCase) ? "sse" : "streamableHttp";
        }

        return "stdio";
    }

    /// <summary>
    /// Validates that the Env dictionary does not contain dangerous environment variable names
    /// (e.g. LD_PRELOAD, DYLD_INSERT_LIBRARIES) that could be used for library injection.
    /// Returns a list of rejected variable names, or an empty list if all are safe.
    /// </summary>
    public IReadOnlyList<string> GetDeniedEnvVarNames()
    {
        if (Env is null or { Count: 0 })
        {
            return [];
        }

        var denied = new List<string>();
        foreach (var key in Env.Keys)
        {
            if (DeniedEnvVars.Contains(key))
            {
                denied.Add(key);
            }
        }

        return denied;
    }
}