namespace Clawsharp.Tools;

internal static class PathGuard
{
    /// <summary>
    ///     Resolves a relative path against the workspace and ensures the result stays within it.
    ///     Prevents path traversal attacks (e.g. "../../etc/passwd").
    /// </summary>
    internal static string SafeResolve(string workspace, string relativePath)
    {
        // Expand ~ to user home directory so LLM-supplied paths like ~/Documents/file.txt resolve correctly.
        // The workspace boundary check below still applies — tilde-expanded paths outside the workspace are rejected.
        if (relativePath == "~")
        {
            relativePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (relativePath.StartsWith("~/", StringComparison.Ordinal))
        {
            relativePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                relativePath[2..]);
        }

        var full = Path.GetFullPath(Path.Combine(workspace, relativePath));
        var normalizedWorkspace = Path.GetFullPath(workspace);

        // Allow the workspace directory itself or anything under it
        if (!IsWithinWorkspace(full, normalizedWorkspace))
        {
            throw new InvalidOperationException($"Access denied: path escapes workspace (resolved to {full}).");
        }

        // Resolve symlinks: walk up to the longest existing ancestor, resolve its
        // symlinks, reconstruct the full path, and re-check the workspace boundary.
        // This prevents symlink escape attacks (e.g. ln -s /etc workspace/link).
        var realPath = ResolveSymlinks(full);
        if (!IsWithinWorkspace(realPath, normalizedWorkspace))
        {
            throw new InvalidOperationException(
                $"Access denied: path escapes workspace via symlink (resolved to {realPath}).");
        }

        return full;
    }

    /// <summary>
    ///     Re-verifies that a previously resolved path has not become a symlink escape.
    ///     Call this immediately before the actual file I/O operation to close the TOCTOU window.
    /// </summary>
    internal static void VerifyNotSymlinkEscape(string resolvedPath, string workspace)
    {
        var normalizedWorkspace = Path.GetFullPath(workspace);

        // Check if the path itself is a symlink
        var fileInfo = new FileInfo(resolvedPath);
        if (fileInfo.LinkTarget is not null)
        {
            var target = Path.GetFullPath(fileInfo.LinkTarget, Path.GetDirectoryName(resolvedPath)!);
            if (!IsWithinWorkspace(target, normalizedWorkspace))
            {
                throw new InvalidOperationException("Access denied: path is a symlink escaping workspace.");
            }
        }

        // Check parent directories for symlink escapes
        var dir = Path.GetDirectoryName(resolvedPath);
        while (dir is not null && dir.Length >= normalizedWorkspace.Length)
        {
            var dirInfo = new DirectoryInfo(dir);
            if (dirInfo.Exists && dirInfo.LinkTarget is not null)
            {
                var target = Path.GetFullPath(dirInfo.LinkTarget, Path.GetDirectoryName(dir)!);
                if (!IsWithinWorkspace(target, normalizedWorkspace))
                {
                    throw new InvalidOperationException("Access denied: parent directory is a symlink escaping workspace.");
                }
            }

            dir = Path.GetDirectoryName(dir);
        }
    }

    /// <summary>
    ///     CRIT-02: After opening a file handle, verify that the file descriptor's actual path
    ///     (resolved via /proc/self/fd/N on Linux) is still within the workspace. This closes
    ///     the TOCTOU race window — even if an attacker replaced a directory with a symlink
    ///     between VerifyNotSymlinkEscape and File.Open, this check catches it.
    /// </summary>
    internal static void VerifyFileDescriptorPath(FileStream fs, string workspace)
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // /proc/self/fd is Linux-only; other OSes rely on VerifyNotSymlinkEscape
        }

        try
        {
            var fdPath = $"/proc/self/fd/{fs.SafeFileHandle.DangerousGetHandle().ToInt32()}";
            var realPath = File.ResolveLinkTarget(fdPath, returnFinalTarget: true)?.FullName;
            if (realPath is null)
            {
                return; // Could not resolve — allow (best-effort)
            }

            var normalizedWorkspace = Path.GetFullPath(workspace);
            if (!IsWithinWorkspace(realPath, normalizedWorkspace))
            {
                throw new InvalidOperationException(
                    $"Access denied: file descriptor resolves outside workspace (real path: {realPath}).");
            }
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw our own security exception
        }
        catch
        {
            // /proc may not be mounted or fd may not be readable — allow (best-effort)
        }
    }

    private static bool IsWithinWorkspace(string path, string normalizedWorkspace) =>
        path.StartsWith(normalizedWorkspace + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || path.Equals(normalizedWorkspace, StringComparison.Ordinal);

    /// <summary>
    ///     Resolves symlinks for the longest existing prefix of <paramref name="path"/>,
    ///     then appends the non-existent tail. This catches symlink escapes in any
    ///     existing directory component, while still allowing creation of new files.
    /// </summary>
    private static string ResolveSymlinks(string path)
    {
        // If the exact path exists, try to resolve it as a symlink directly.
        // ResolveLinkTarget throws if the path does not exist, so check first.
        if (File.Exists(path))
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target is not null)
            {
                return Path.GetFullPath(target.FullName);
            }

            return path;
        }

        if (Directory.Exists(path))
        {
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                return Path.GetFullPath(target.FullName);
            }

            return path;
        }

        // Path doesn't exist yet: walk up to find the deepest existing ancestor,
        // resolve its symlinks, and re-append the non-existent tail.
        var current = path;
        var tailSegments = new Stack<string>();

        while (true)
        {
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                // Reached filesystem root — nothing to resolve
                break;
            }

            tailSegments.Push(Path.GetFileName(current)!);
            current = parent;

            if (Directory.Exists(current))
            {
                // Found an existing ancestor — resolve its symlinks
                var ancestorTarget = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
                string resolvedAncestor;
                if (ancestorTarget is not null)
                {
                    resolvedAncestor = Path.GetFullPath(ancestorTarget.FullName);
                }
                else
                {
                    resolvedAncestor = current;
                }

                // Reconstruct with the non-existent tail
                while (tailSegments.Count > 0)
                {
                    resolvedAncestor = Path.Combine(resolvedAncestor, tailSegments.Pop());
                }

                return Path.GetFullPath(resolvedAncestor);
            }
        }

        // No existing ancestor found — return the original (nothing to resolve)
        return path;
    }
}