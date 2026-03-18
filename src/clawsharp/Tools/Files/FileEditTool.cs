using System.Text.Json;
using Clawsharp.Security;

namespace Clawsharp.Tools.Files;

public sealed class FileEditTool(string workspace, AuditLogger? auditLogger = null) : Tool
{
    private readonly string _workspace = Path.GetFullPath(workspace);

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "file_edit";

    public override string Description => "Find and replace text within a file. Path is relative to the workspace.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "path":        { "type": "string",  "description": "File path relative to workspace" },
                                                       "old_text":    { "type": "string",  "description": "Exact text to find" },
                                                       "new_text":    { "type": "string",  "description": "Replacement text" },
                                                       "replace_all": { "type": "boolean", "description": "Replace all occurrences (default: false — replaces first only)" }
                                                     },
                                                     "required": ["path", "old_text", "new_text"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var workspaceError = WorkspaceGuard.CheckAvailability(_workspace);
        if (workspaceError is not null)
        {
            return workspaceError;
        }

        var rel = arguments.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        var oldText = arguments.TryGetProperty("old_text", out var o) ? o.GetString() ?? "" : "";
        var newText = arguments.TryGetProperty("new_text", out var n) ? n.GetString() ?? "" : "";
        var replaceAll = arguments.TryGetProperty("replace_all", out var r) && r.GetBoolean();

        if (string.IsNullOrEmpty(oldText))
        {
            return "Error: old_text must not be empty.";
        }

        string fullPath;
        try
        {
            fullPath = PathGuard.SafeResolve(_workspace, rel);
        }
        catch (InvalidOperationException ex)
        {
            if (auditLogger is not null)
            {
                _ = auditLogger.LogFileAccessAsync(rel, "file_edit", ChannelName, success: false, error: ex.Message, ct: ct);
            }

            return "Error: path is outside the workspace.";
        }

        if (!File.Exists(fullPath))
        {
            return $"Error: file not found: {rel}";
        }

        // TOCTOU re-check: verify path has not become a symlink escape since SafeResolve
        try
        {
            PathGuard.VerifyNotSymlinkEscape(fullPath, _workspace);
        }
        catch (InvalidOperationException)
        {
            return "Error: path is outside the workspace.";
        }

        var content = await File.ReadAllTextAsync(fullPath, ct);

        var idx = content.IndexOf(oldText, StringComparison.Ordinal);
        if (idx < 0)
        {
            return $"Error: old_text not found in {rel}";
        }

        string updated;
        int count;
        if (replaceAll)
        {
            count = CountOccurrences(content, oldText);
            updated = content.Replace(oldText, newText, StringComparison.Ordinal);
        }
        else
        {
            count = 1;
            updated = string.Concat(content.AsSpan(0, idx), newText, content.AsSpan(idx + oldText.Length));
        }

        await File.WriteAllTextAsync(fullPath, updated, ct);

        if (auditLogger is not null)
        {
            _ = auditLogger.LogFileAccessAsync(rel, "file_edit", ChannelName, success: true, ct: ct);
        }

        return $"Replaced {count} occurrence(s) in {rel}";
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }

        return count;
    }
}