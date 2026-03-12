using Clawsharp.Core;

namespace Clawsharp.Tools;

/// <summary>Abstraction over tool registration, definition listing, and execution.</summary>
public interface IToolRegistry
{
    /// <summary>Registers a tool dynamically (e.g. from an MCP server).</summary>
    void Register(Tool tool);

    void SetChannelContext(string channelName, int spawnDepth = 0, string? sessionId = null);

    IReadOnlyList<ToolDefinition> GetDefinitions();

    /// <summary>
    /// Returns tool definitions filtered by the configured filter groups.
    /// Tools in "always" groups are always included. Tools in "dynamic" groups
    /// are included only when <paramref name="messageText"/> contains one of the
    /// group's keywords. Tools not matched by any group are always included.
    /// </summary>
    IReadOnlyList<ToolDefinition> GetFilteredDefinitions(string? messageText);

    Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken ct = default);
}