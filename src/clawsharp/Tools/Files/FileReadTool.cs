using System.Text.Json;
using Clawsharp.Security;

namespace Clawsharp.Tools.Files;

public sealed class FileReadTool : Tool
{
    private readonly string _workspace;

    private readonly AuditLogger? _auditLogger;

    public FileReadTool(string workspace, AuditLogger? auditLogger = null)
    {
        _workspace = Path.GetFullPath(workspace);
        _auditLogger = auditLogger;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "file_read";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description => "Read the contents of a file. Path is relative to the workspace.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "path": { "type": "string", "description": "File path relative to workspace" }
                                                     },
                                                     "required": ["path"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var workspaceError = WorkspaceGuard.CheckAvailability(_workspace);
        if (workspaceError is not null)
            return workspaceError;

        var rel = arguments.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";

        string path;
        try
        {
            path = PathGuard.SafeResolve(_workspace, rel);
        }
        catch (InvalidOperationException ex)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(rel, "read", ChannelName, success: false, error: ex.Message, ct: ct);
            }

            return $"Error: {ex.Message}";
        }

        if (!File.Exists(path))
        {
            return $"Error: file not found: {rel}";
        }

        // Size guard — prevent reading arbitrarily large files into memory
        const long maxFileBytes = 512 * 1024; // 512 KB
        const int maxChars = 131_072; // 128K chars for context
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > maxFileBytes)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(rel, "read", ChannelName, success: false,
                    error: $"File too large: {fileInfo.Length:N0} bytes (max {maxFileBytes:N0})", ct: ct);
            }

            return
                $"Error: file too large ({fileInfo.Length:N0} bytes, max {maxFileBytes / 1024}KB). Use a more specific tool or read a portion of the file.";
        }

        // TOCTOU re-check: verify path has not become a symlink escape since SafeResolve
        try
        {
            PathGuard.VerifyNotSymlinkEscape(path, _workspace);
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var content = await File.ReadAllTextAsync(path, ct);
        if (content.Length > maxChars)
        {
            content = content[..maxChars] + $"\n... (truncated at {maxChars:N0} chars, file has {content.Length:N0} total)";
        }

        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogFileAccessAsync(rel, "read", ChannelName, success: true, ct: ct);
        }

        return content;
    }
}