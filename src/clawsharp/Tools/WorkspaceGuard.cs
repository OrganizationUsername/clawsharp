namespace Clawsharp.Tools;

internal static class WorkspaceGuard
{
    private const string NoWorkspaceMessage =
        """
        No workspace is available. The workspace directory exists but is empty.
        To give the agent access to files, either:
           1. Mount a host directory: -v /path/to/project:/home/app/.clawsharp/workspace
           2. Connect an IDE MCP server (Rider, VS Code) for remote file access
           
        See the documentation for setup details.
        """;

    /// <summary>
    /// Returns an error message if the workspace appears unmounted (exists but is completely empty).
    /// Returns null if the workspace has content or does not exist (PathGuard handles the latter).
    /// </summary>
    internal static string? CheckAvailability(string workspace)
    {
        if (!Directory.Exists(workspace))
        {
            return null; // PathGuard will handle missing directory errors
        }

        // A workspace with any file or subdirectory is considered available.
        // EnumerateFileSystemEntries is lazy — stops at first entry.
        if (Directory.EnumerateFileSystemEntries(workspace).Any())
        {
            return null;
        }

        return NoWorkspaceMessage;
    }
}
