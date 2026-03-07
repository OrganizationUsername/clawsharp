using Clawsharp.Tools;

namespace Clawsharp.Tests;

public sealed class PathGuardTests
{
    private string _workspace = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_workspace);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Test]
    public void SafeResolve_NormalRelativePath_ReturnsFullPath()
    {
        var result = PathGuard.SafeResolve(_workspace, "subdir/file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "subdir/file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_EmptyPath_ReturnsWorkspaceRoot()
    {
        var result = PathGuard.SafeResolve(_workspace, "");

        result.ShouldBe(Path.GetFullPath(_workspace));
    }

    [Test]
    public void SafeResolve_FileInRoot_ReturnsCorrectPath()
    {
        var result = PathGuard.SafeResolve(_workspace, "file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_NestedPath_ReturnsCorrectPath()
    {
        var result = PathGuard.SafeResolve(_workspace, "subdir/nested/file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "subdir/nested/file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_DotDotTraversal_ThrowsInvalidOperation()
    {
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "../../etc/passwd"));
    }

    [Test]
    public void SafeResolve_ForwardSlashTraversal_ThrowsInvalidOperation()
    {
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "../escape"));
    }

    [Test]
    public void SafeResolve_AbsolutePathInsideWorkspace_ReturnsPath()
    {
        var absPath = Path.Combine(_workspace, "subdir", "file.txt");

        var result = PathGuard.SafeResolve(_workspace, absPath);

        result.ShouldBe(Path.GetFullPath(absPath));
    }

    [Test]
    public void SafeResolve_AbsolutePathOutsideWorkspace_ThrowsInvalidOperation()
    {
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "/etc/passwd"));
    }

    [Test]
    public void SafeResolve_RedundantDotSlash_ResolvesCorrectly()
    {
        var result = PathGuard.SafeResolve(_workspace, "./subdir/../subdir/file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "subdir/file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_TildeSlashPath_ExpandsHomeDirectory()
    {
        // Use the actual home directory as the workspace so the expanded path is within it
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var result = PathGuard.SafeResolve(home, "~/Documents/file.txt");

        var expected = Path.GetFullPath(Path.Combine(home, "Documents/file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_TildeAlone_ExpandsToHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var result = PathGuard.SafeResolve(home, "~");

        result.ShouldBe(Path.GetFullPath(home));
    }

    [Test]
    public void SafeResolve_TildePathOutsideWorkspace_ThrowsInvalidOperation()
    {
        // Workspace is a temp dir, so ~/anything expands to home dir which is outside it
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "~/Documents/file.txt"));
    }

    [Test]
    public void SafeResolve_TildeTraversal_ThrowsInvalidOperation()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Even with home as workspace, ../../etc/passwd should still be rejected
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(home, "~/../../etc/passwd"));
    }

    // --- VerifyNotSymlinkEscape tests ---

    [Test]
    public void VerifyNotSymlinkEscape_RegularFileInsideWorkspace_DoesNotThrow()
    {
        var filePath = Path.Combine(_workspace, "normal.txt");
        File.WriteAllText(filePath, "content");

        Should.NotThrow(() => PathGuard.VerifyNotSymlinkEscape(filePath, _workspace));
    }

    [Test]
    public void VerifyNotSymlinkEscape_NonExistentPath_DoesNotThrow()
    {
        // Non-existent paths have no symlink to detect — should pass silently
        var filePath = Path.Combine(_workspace, "does-not-exist.txt");

        Should.NotThrow(() => PathGuard.VerifyNotSymlinkEscape(filePath, _workspace));
    }

    [Test]
    public void VerifyNotSymlinkEscape_SymlinkInsideWorkspace_DoesNotThrow()
    {
        // Symlink pointing to another file INSIDE the workspace is safe
        var targetPath = Path.Combine(_workspace, "real.txt");
        File.WriteAllText(targetPath, "content");

        var linkPath = Path.Combine(_workspace, "link.txt");
        File.CreateSymbolicLink(linkPath, targetPath);

        Should.NotThrow(() => PathGuard.VerifyNotSymlinkEscape(linkPath, _workspace));
    }

    [Test]
    public void VerifyNotSymlinkEscape_SymlinkEscapingWorkspace_ThrowsInvalidOperation()
    {
        // Symlink pointing OUTSIDE the workspace should be rejected
        var linkPath = Path.Combine(_workspace, "escape-link.txt");
        File.CreateSymbolicLink(linkPath, "/etc/hostname");

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.VerifyNotSymlinkEscape(linkPath, _workspace));
    }

    [Test]
    public void VerifyNotSymlinkEscape_ParentDirSymlinkEscaping_ThrowsInvalidOperation()
    {
        // Create a directory symlink pointing outside the workspace
        var linkDir = Path.Combine(_workspace, "escape-dir");
        Directory.CreateSymbolicLink(linkDir, "/tmp");

        var filePath = Path.Combine(linkDir, "somefile.txt");

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.VerifyNotSymlinkEscape(filePath, _workspace));
    }

    [Test]
    public void VerifyNotSymlinkEscape_ParentDirSymlinkInsideWorkspace_DoesNotThrow()
    {
        // Directory symlink pointing to another directory INSIDE the workspace is safe
        var targetDir = Path.Combine(_workspace, "real-dir");
        Directory.CreateDirectory(targetDir);

        var linkDir = Path.Combine(_workspace, "link-dir");
        Directory.CreateSymbolicLink(linkDir, targetDir);

        var filePath = Path.Combine(linkDir, "somefile.txt");

        Should.NotThrow(() => PathGuard.VerifyNotSymlinkEscape(filePath, _workspace));
    }
}