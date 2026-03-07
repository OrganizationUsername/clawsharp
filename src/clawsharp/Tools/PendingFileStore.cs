namespace Clawsharp.Tools;

/// <summary>
/// A file queued for delivery to the user via the active channel.
/// Produced by <see cref="SendFileTool"/> and consumed by AgentLoop after tool execution.
/// </summary>
internal sealed record PendingFile(
    string Filename,
    ReadOnlyMemory<byte> Content,
    string? Message);

/// <summary>
/// Thread-safe, per-async-flow store for files that tools want to send to the user.
/// Uses <see cref="AsyncLocal{T}"/> so each AgentLoop call chain gets its own isolated queue,
/// matching the pattern used by <see cref="ToolRegistry.CurrentChannelName"/>.
/// </summary>
internal static class PendingFileStore
{
    private static readonly AsyncLocal<List<PendingFile>?> _files = new();

    /// <summary>Enqueues a file for delivery after the current tool-call batch completes.</summary>
    internal static void Enqueue(PendingFile file)
    {
        _files.Value ??= [];
        _files.Value.Add(file);
    }

    /// <summary>Drains and returns all pending files for the current async flow, clearing the queue.</summary>
    internal static List<PendingFile> DrainAll()
    {
        var files = _files.Value;
        _files.Value = null;
        return files ?? [];
    }
}