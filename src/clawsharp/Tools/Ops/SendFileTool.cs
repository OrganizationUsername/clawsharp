using System.Text.Json;
using Clawsharp.Security;

namespace Clawsharp.Tools.Ops;

/// <summary>
/// Tool that allows the LLM to send a file from the workspace to the user
/// via the active channel. The file is enqueued in <see cref="PendingFileStore"/>
/// and delivered by AgentLoop after the tool-call batch completes.
/// </summary>
internal sealed class SendFileTool : Tool
{
    private readonly string _workspace;

    private readonly AuditLogger? _auditLogger;

    /// <summary>Maximum file size that can be sent (8 MB — safe limit for most platforms).</summary>
    private const long MaxFileSizeBytes = 8 * 1024 * 1024;

    public SendFileTool(string workspace, AuditLogger? auditLogger = null)
    {
        _workspace = Path.GetFullPath(workspace);
        _auditLogger = auditLogger;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "send_file";

    public override string Description =>
        "Send a file from the workspace to the user via the active channel. " +
        "Supports files up to 8 MB. For channels that support file uploads (Slack, Discord), " +
        "the file is uploaded natively. For other channels, the file path and metadata are returned.";

    public override string ParametersSchemaJson => """
                                                   {
                                                       "type": "object",
                                                       "properties": {
                                                           "path": {
                                                               "type": "string",
                                                               "description": "Path to the file to send (relative to workspace or absolute)"
                                                           },
                                                           "message": {
                                                               "type": "string",
                                                               "description": "Optional message to accompany the file"
                                                           }
                                                       },
                                                       "required": ["path"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var rel = arguments.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        var message = arguments.TryGetProperty("message", out var m) ? m.GetString() : null;

        // Resolve path safely within workspace
        string fullPath;
        try
        {
            fullPath = PathGuard.SafeResolve(_workspace, rel);
        }
        catch (InvalidOperationException ex)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(rel, "send_file", ChannelName, success: false,
                    error: ex.Message, ct: ct);
            }

            return $"Error: {ex.Message}";
        }

        if (!File.Exists(fullPath))
        {
            return $"Error: file not found: {rel}";
        }

        // Check file size
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(rel, "send_file", ChannelName, success: false,
                    error: $"File too large: {fileInfo.Length:N0} bytes (max {MaxFileSizeBytes:N0})", ct: ct);
            }

            return $"Error: file too large ({fileInfo.Length:N0} bytes, max {MaxFileSizeBytes / (1024 * 1024)} MB).";
        }

        if (fileInfo.Length == 0)
        {
            return $"Error: file is empty: {rel}";
        }

        // Read the file bytes
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, ct);
        }
        catch (Exception ex)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(rel, "send_file", ChannelName, success: false,
                    error: ex.Message, ct: ct);
            }

            return $"Error reading file: {ex.Message}";
        }

        var filename = Path.GetFileName(fullPath);

        // Enqueue the file for delivery by AgentLoop
        PendingFileStore.Enqueue(new PendingFile(filename, bytes, message));

        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogFileAccessAsync(rel, "send_file", ChannelName, success: true, ct: ct);
        }

        return $"File queued for delivery: {filename} ({bytes.Length:N0} bytes). " +
               "It will be sent to the user when the current response completes.";
    }
}