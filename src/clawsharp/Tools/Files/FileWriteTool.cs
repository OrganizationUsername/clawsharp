using System.Text;
using System.Text.Json;
using Clawsharp.Security;

namespace Clawsharp.Tools.Files;

public sealed class FileWriteTool : Tool
{
    private const int MaxWriteBytes = 10 * 1024 * 1024; // 10 MB

    private readonly string _workspace;

    private readonly AuditLogger? _auditLogger;

    public FileWriteTool(string workspace, AuditLogger? auditLogger = null)
    {
        _workspace = Path.GetFullPath(workspace);
        _auditLogger = auditLogger;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "file_write";

    public override string Description => "Write content to a file. Creates directories as needed. Path is relative to the workspace.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "path": { "type": "string", "description": "File path relative to workspace" },
                                                       "content": { "type": "string", "description": "Content to write" },
                                                       "append": { "type": "boolean", "description": "Append to file instead of overwriting (default false)" }
                                                     },
                                                     "required": ["path", "content"]
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
        var content = arguments.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var append = arguments.TryGetProperty("append", out var a) && a.GetBoolean();

        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (byteCount > MaxWriteBytes)
        {
            return $"Error: content too large ({byteCount / (1024 * 1024)} MB). Maximum write size is 10 MB.";
        }

        string fullPath;
        try
        {
            fullPath = PathGuard.SafeResolve(_workspace, rel);
        }
        catch (InvalidOperationException ex)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(rel, "write", ChannelName, success: false, error: ex.Message, ct: ct);
            }

            return "Error: path is outside the workspace.";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // TOCTOU re-check: verify path has not become a symlink escape since SafeResolve
        try
        {
            PathGuard.VerifyNotSymlinkEscape(fullPath, _workspace);
        }
        catch (InvalidOperationException)
        {
            return "Error: path is outside the workspace.";
        }

        // CRIT-02: Open the file handle, then verify the actual path via /proc/self/fd/
        // to close the TOCTOU race window between VerifyNotSymlinkEscape and file I/O.
        // On non-Linux, the VerifyNotSymlinkEscape check above is the best we can do.
        if (append)
        {
            await using var fs = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.None);
            PathGuard.VerifyFileDescriptorPath(fs, _workspace);
            await using var writer = new StreamWriter(fs);
            await writer.WriteAsync(content.AsMemory(), ct);
        }
        else
        {
            await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            PathGuard.VerifyFileDescriptorPath(fs, _workspace);
            await using var writer = new StreamWriter(fs);
            await writer.WriteAsync(content.AsMemory(), ct);
        }

        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogFileAccessAsync(rel, append ? "append" : "write", ChannelName, success: true, ct: ct);
        }

        return $"Written {content.Length} chars to {rel}";
    }
}