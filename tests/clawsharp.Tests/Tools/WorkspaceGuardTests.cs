using Clawsharp.Tools;

namespace Clawsharp.Tests.Tools;

[TestFixture]
public sealed class WorkspaceGuardTests : IDisposable
{
    private readonly string _tempDir;

    public WorkspaceGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            $"clawsharp-wg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Test]
    public void CheckAvailability_NonExistentDirectory_ReturnsNull()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        WorkspaceGuard.CheckAvailability(missing).ShouldBeNull(
            "non-existent directory returns null — PathGuard handles the actual error");
    }

    [Test]
    public void CheckAvailability_EmptyDirectory_ReturnsErrorMessage()
    {
        var empty = Path.Combine(_tempDir, "empty-workspace");
        Directory.CreateDirectory(empty);

        var result = WorkspaceGuard.CheckAvailability(empty);

        result.ShouldNotBeNull("empty workspace must return an error message");
        result.ShouldContain("workspace", Case.Insensitive);
    }

    [Test]
    public void CheckAvailability_EmptyDirectory_MessageContainsMountGuidance()
    {
        var empty = Path.Combine(_tempDir, "empty-ws-2");
        Directory.CreateDirectory(empty);

        var result = WorkspaceGuard.CheckAvailability(empty)!;

        (result.Contains("-v", StringComparison.Ordinal) ||
         result.Contains("Mount", StringComparison.OrdinalIgnoreCase) ||
         result.Contains("mount", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("message should contain Docker volume mount guidance");
    }

    [Test]
    public void CheckAvailability_DirectoryWithFile_ReturnsNull()
    {
        var ws = Path.Combine(_tempDir, "ws-with-file");
        Directory.CreateDirectory(ws);
        File.WriteAllText(Path.Combine(ws, "readme.txt"), "hello");

        WorkspaceGuard.CheckAvailability(ws).ShouldBeNull();
    }

    [Test]
    public void CheckAvailability_DirectoryWithHiddenFile_ReturnsNull()
    {
        var ws = Path.Combine(_tempDir, "ws-hidden");
        Directory.CreateDirectory(ws);
        File.WriteAllText(Path.Combine(ws, ".hidden"), "hidden content");

        WorkspaceGuard.CheckAvailability(ws).ShouldBeNull();
    }

    [Test]
    public void CheckAvailability_DirectoryWithSubdirectoryOnly_ReturnsNull()
    {
        var ws = Path.Combine(_tempDir, "ws-subdir");
        Directory.CreateDirectory(ws);
        Directory.CreateDirectory(Path.Combine(ws, "subdir"));

        WorkspaceGuard.CheckAvailability(ws).ShouldBeNull(
            "a workspace with a subdirectory is considered mounted/available");
    }

    [Test]
    public void CheckAvailability_CalledRepeatedly_ReturnsSameResult()
    {
        var empty = Path.Combine(_tempDir, "ws-idempotent");
        Directory.CreateDirectory(empty);

        var first = WorkspaceGuard.CheckAvailability(empty);
        var second = WorkspaceGuard.CheckAvailability(empty);

        second.ShouldBe(first);
    }
}
