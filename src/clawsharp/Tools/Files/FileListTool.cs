using System.Text.Json;

namespace Clawsharp.Tools.Files;

public sealed class FileListTool(string workspace) : Tool
{
    private const int MaxListEntries = 500;

    private readonly string _workspace = Path.GetFullPath(workspace);

    public override string Name => "file_list";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description => "List files in a directory. Path is relative to the workspace.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "path": { "type": "string", "description": "Directory path relative to workspace (default: root)" },
                                                       "recursive": { "type": "boolean", "description": "List recursively (default false)" }
                                                     }
                                                   }
                                                   """;

    public override Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var workspaceError = WorkspaceGuard.CheckAvailability(_workspace);
        if (workspaceError is not null)
        {
            return Task.FromResult(workspaceError);
        }

        var rel = arguments.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        var recursive = arguments.TryGetProperty("recursive", out var r) && r.GetBoolean();

        string fullPath;
        try
        {
            fullPath = PathGuard.SafeResolve(_workspace, rel);
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult("Error: path is outside the workspace.");
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult($"Error: directory not found: {rel}");
        }

        // TOCTOU re-check: verify path has not become a symlink escape since SafeResolve
        try
        {
            PathGuard.VerifyNotSymlinkEscape(fullPath, _workspace);
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult("Error: path is outside the workspace.");
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.GetFileSystemEntries(fullPath, "*", option)
                               .Select(e => Path.GetRelativePath(_workspace, e))
                               .OrderBy(e => e)
                               .Take(MaxListEntries)
                               .ToList();

        return Task.FromResult(string.Join("\n", entries));
    }
}