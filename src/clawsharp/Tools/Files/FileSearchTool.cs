using System.Text.Json;

using Clawsharp.Tools;
namespace Clawsharp.Tools.Files;

public sealed class FileSearchTool : Tool
{
    private const int MaxSearchResults = 100;

    private const long MaxFileSizeBytes = 1 * 1024 * 1024;

    private readonly string _workspace;

    public FileSearchTool(string workspace)
    {
        _workspace = Path.GetFullPath(workspace);
    }

    public override string Name => "file_search";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description => "Search for text in files within the workspace using grep-like matching.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "pattern": { "type": "string", "description": "Text pattern to search for" },
                                                       "path": { "type": "string", "description": "Directory to search in (default: root)" },
                                                       "file_pattern": { "type": "string", "description": "File name pattern e.g. *.cs (default: *)" }
                                                     },
                                                     "required": ["pattern"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var workspaceError = WorkspaceGuard.CheckAvailability(_workspace);
        if (workspaceError is not null)
            return workspaceError;

        var pattern = arguments.TryGetProperty("pattern", out var pt) ? pt.GetString() ?? "" : "";
        var rel = arguments.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        var filePattern = arguments.TryGetProperty("file_pattern", out var fp) ? fp.GetString() ?? "*" : "*";

        string dir;
        try
        {
            dir = PathGuard.SafeResolve(_workspace, rel);
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        if (!Directory.Exists(dir))
        {
            return $"Error: directory not found: {rel}";
        }

        // TOCTOU re-check: verify path has not become a symlink escape since SafeResolve
        try
        {
            PathGuard.VerifyNotSymlinkEscape(dir, _workspace);
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var results = new List<string>();
        var skipDirs = new HashSet<string>(["bin", "obj", ".git", "node_modules", ".vs"], StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dir, filePattern, SearchOption.AllDirectories))
        {
            // Skip noise directories
            var parts = Path.GetRelativePath(dir, file).Split(Path.DirectorySeparatorChar);
            if (parts.Any(part => skipDirs.Contains(part)))
            {
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > MaxFileSizeBytes) // skip files > 1 MB
                {
                    continue;
                }

                var lines = await File.ReadAllLinesAsync(file, ct);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var relFile = Path.GetRelativePath(_workspace, file);
                        results.Add($"{relFile}:{i + 1}: {lines[i].Trim()}");
                        if (results.Count >= MaxSearchResults)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                /* skip unreadable files */
            }

            if (results.Count >= MaxSearchResults)
            {
                break;
            }
        }

        return results.Count > 0
            ? string.Join("\n", results)
            : "No matches found.";
    }
}