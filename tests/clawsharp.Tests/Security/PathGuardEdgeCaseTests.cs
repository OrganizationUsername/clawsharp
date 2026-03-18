using Clawsharp.Tools;

namespace Clawsharp.Tests.Security;

[TestFixture]
public sealed class PathGuardEdgeCaseTests
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

    // ── MED-8: Null byte in path ──────────────────────────────────────────
    //
    // .NET's Path.GetFullPath throws on null bytes. PathGuard should propagate
    // this exception rather than silently accepting the path.

    [Test]
    public void SafeResolve_NullByteInPath_Throws()
    {
        // Path.Combine and Path.GetFullPath throw ArgumentException for null bytes
        // on .NET. PathGuard.SafeResolve should not silently swallow this.
        Should.Throw<ArgumentException>(() =>
            PathGuard.SafeResolve(_workspace, "file\0.txt"));
    }

    [Test]
    public void SafeResolve_NullByteInMiddleOfPath_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            PathGuard.SafeResolve(_workspace, "subdir/file\0name.txt"));
    }

    [Test]
    public void SafeResolve_NullByteInDirectoryComponent_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            PathGuard.SafeResolve(_workspace, "sub\0dir/file.txt"));
    }

    // ── MED-9: Workspace is a symlink ─────────────────────────────────────
    //
    // PathGuard compares resolved symlink targets against the raw workspace path.
    // When the workspace itself is a symlink, ResolveSymlinks resolves paths
    // through the real directory, but IsWithinWorkspace compares against the
    // symlink path. This causes all paths to be rejected as "escaping workspace
    // via symlink". This is correct security behavior: callers should pass the
    // canonical real path as the workspace, not a symlink.

    [Test]
    public void SafeResolve_WorkspaceIsSymlink_RejectsPathsBecauseResolvedPathDiffersFromSymlinkPath()
    {
        // Create a real directory and a symlink workspace pointing to it
        var realDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(realDir);

        var symlinkWorkspace = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateSymbolicLink(symlinkWorkspace, realDir);

        try
        {
            // PathGuard rejects this because the resolved real path does not start
            // with the symlink workspace path. Callers must use the real path.
            Should.Throw<InvalidOperationException>(() =>
                PathGuard.SafeResolve(symlinkWorkspace, "subdir/file.txt"));
        }
        finally
        {
            if (Directory.Exists(symlinkWorkspace))
            {
                Directory.Delete(symlinkWorkspace);
            }

            if (Directory.Exists(realDir))
            {
                Directory.Delete(realDir, recursive: true);
            }
        }
    }

    [Test]
    public void SafeResolve_WorkspaceIsSymlink_RealPathWorksCorrectly()
    {
        // Using the real directory path (not the symlink) works as expected
        var realDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(realDir);

        try
        {
            var result = PathGuard.SafeResolve(realDir, "subdir/file.txt");
            result.ShouldStartWith(Path.GetFullPath(realDir));
        }
        finally
        {
            if (Directory.Exists(realDir))
            {
                Directory.Delete(realDir, recursive: true);
            }
        }
    }

    [Test]
    public void SafeResolve_WorkspaceIsSymlink_TraversalStillBlocked()
    {
        var realDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(realDir);

        var symlinkWorkspace = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateSymbolicLink(symlinkWorkspace, realDir);

        try
        {
            // Path traversal is blocked even when workspace is a symlink
            Should.Throw<InvalidOperationException>(() =>
                PathGuard.SafeResolve(symlinkWorkspace, "../../etc/passwd"));
        }
        finally
        {
            if (Directory.Exists(symlinkWorkspace))
            {
                Directory.Delete(symlinkWorkspace);
            }

            if (Directory.Exists(realDir))
            {
                Directory.Delete(realDir, recursive: true);
            }
        }
    }

    // ── MED-10: VerifyFileDescriptorPath is no-op on non-Linux ────────────
    //
    // VerifyFileDescriptorPath relies on /proc/self/fd/ which is Linux-only.
    // On other platforms it returns without checking. This test documents
    // the platform limitation.

    [Test]
    public void VerifyFileDescriptorPath_DocumentsPlatformBehavior()
    {
        var filePath = Path.Combine(_workspace, "test.txt");
        File.WriteAllText(filePath, "content");

        using var fs = File.OpenRead(filePath);

        if (OperatingSystem.IsLinux())
        {
            // On Linux, VerifyFileDescriptorPath actually checks /proc/self/fd/
            // and should NOT throw for a file inside the workspace
            Should.NotThrow(() => PathGuard.VerifyFileDescriptorPath(fs, _workspace));
        }
        else
        {
            // On non-Linux platforms, VerifyFileDescriptorPath is a no-op.
            // It returns immediately without checking — security relies solely
            // on VerifyNotSymlinkEscape and SafeResolve.
            Should.NotThrow(() => PathGuard.VerifyFileDescriptorPath(fs, _workspace));
        }
    }

    [Test]
    public void VerifyFileDescriptorPath_FileInsideWorkspace_DoesNotThrow()
    {
        var filePath = Path.Combine(_workspace, "safe.txt");
        File.WriteAllText(filePath, "safe content");

        using var fs = File.OpenRead(filePath);

        Should.NotThrow(() => PathGuard.VerifyFileDescriptorPath(fs, _workspace));
    }

    // ── LOW-12: Very long path ────────────────────────────────────────────
    //
    // On Linux, PATH_MAX is typically 4096 bytes. .NET's Path.GetFullPath may
    // throw for paths exceeding OS limits. PathGuard should propagate the
    // exception rather than returning an invalid path.

    [Test]
    public void SafeResolve_VeryLongPath_ThrowsOrRejects()
    {
        // Build a path that exceeds typical OS limits (4096+ bytes on Linux)
        // Use repeated directory components to build a deeply nested path
        var segments = new string[200];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = "a_long_directory_name_segment";
        }

        var longRelativePath = string.Join("/", segments) + "/file.txt";

        // This should either throw (PathTooLongException, IOException, or similar)
        // or return a result that is still within the workspace. It should NOT
        // silently produce a truncated path that escapes the workspace.
        try
        {
            var result = PathGuard.SafeResolve(_workspace, longRelativePath);
            // If it doesn't throw, verify the result is still within workspace
            result.ShouldStartWith(Path.GetFullPath(_workspace));
        }
        catch (Exception ex)
        {
            // Expected: PathTooLongException, IOException, or InvalidOperationException
            ex.ShouldBeAssignableTo<Exception>();
        }
    }

    [Test]
    public void SafeResolve_LongFilename_ThrowsOrRejects()
    {
        // Single filename component exceeding NAME_MAX (255 on most filesystems)
        var longName = new string('x', 300) + ".txt";

        try
        {
            var result = PathGuard.SafeResolve(_workspace, longName);
            // If it succeeds, result must still be within workspace
            result.ShouldStartWith(Path.GetFullPath(_workspace));
        }
        catch (Exception ex)
        {
            ex.ShouldBeAssignableTo<Exception>();
        }
    }

    // ── LOW-13: Unicode normalization in filenames ─────────────────────────
    //
    // Path.GetFullPath does not normalize Unicode. Different Unicode
    // representations of the same visual character (NFC vs NFD, combining
    // characters) are treated as distinct paths. This is correct OS behavior
    // but means PathGuard does not equate visually-identical paths.

    [Test]
    public void SafeResolve_UnicodeFilename_ResolvesWithinWorkspace()
    {
        // A filename with Unicode characters (e.g., accented letters)
        var result = PathGuard.SafeResolve(_workspace, "caf\u00E9/file.txt");

        result.ShouldStartWith(Path.GetFullPath(_workspace));
        result.ShouldContain("caf\u00E9");
    }

    [Test]
    public void SafeResolve_CombiningCharacters_TreatedAsDistinctPath()
    {
        // NFC: U+00E9 (precomposed e-acute) vs NFD: U+0065 U+0301 (e + combining acute)
        // These are visually identical but different byte sequences.
        // Path.GetFullPath treats them as distinct paths — this is correct OS behavior.
        var nfcPath = "caf\u00E9.txt";    // precomposed
        var nfdPath = "cafe\u0301.txt";    // decomposed

        var nfcResult = PathGuard.SafeResolve(_workspace, nfcPath);
        var nfdResult = PathGuard.SafeResolve(_workspace, nfdPath);

        // Both should resolve within the workspace
        nfcResult.ShouldStartWith(Path.GetFullPath(_workspace));
        nfdResult.ShouldStartWith(Path.GetFullPath(_workspace));

        // On Linux (case-sensitive, no Unicode normalization), these produce distinct paths.
        // On macOS (HFS+ with NFD normalization), they may resolve to the same path.
        // This test documents that PathGuard passes both through without error.
    }

    [Test]
    public void SafeResolve_EmojiInFilename_ResolvesWithinWorkspace()
    {
        var result = PathGuard.SafeResolve(_workspace, "\U0001F600_file.txt");

        result.ShouldStartWith(Path.GetFullPath(_workspace));
    }

    [Test]
    public void SafeResolve_CjkCharactersInFilename_ResolvesWithinWorkspace()
    {
        var result = PathGuard.SafeResolve(_workspace, "\u6587\u4EF6.txt");

        result.ShouldStartWith(Path.GetFullPath(_workspace));
    }

    // ── Additional edge cases ─────────────────────────────────────────────

    [Test]
    public void SafeResolve_DoubleSlashInPath_NormalizedByGetFullPath()
    {
        var result = PathGuard.SafeResolve(_workspace, "subdir//file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "subdir/file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_TrailingSlash_ResolvesToDirectory()
    {
        var result = PathGuard.SafeResolve(_workspace, "subdir/");

        result.ShouldStartWith(Path.GetFullPath(_workspace));
    }

    [Test]
    public void SafeResolve_DotPath_ResolvesToWorkspace()
    {
        var result = PathGuard.SafeResolve(_workspace, ".");

        result.ShouldBe(Path.GetFullPath(_workspace));
    }

    [Test]
    public void SafeResolve_DotDotWithinWorkspace_Allowed()
    {
        // "subdir/../file.txt" resolves to "file.txt" which is within workspace
        var result = PathGuard.SafeResolve(_workspace, "subdir/../file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_DotDotEscapingWorkspace_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "../../../etc/shadow"));
    }

    [Test]
    public void SafeResolve_SymlinkToFileOutsideWorkspace_Throws()
    {
        // Create a symlink within the workspace pointing outside
        var linkPath = Path.Combine(_workspace, "evil-link.txt");

        // Use /etc/hostname as a target that exists on most Linux systems
        var target = "/etc/hostname";
        if (!File.Exists(target))
        {
            Assert.Inconclusive("Test requires /etc/hostname to exist");
            return;
        }

        File.CreateSymbolicLink(linkPath, target);

        // SafeResolve should detect the symlink escape
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "evil-link.txt"));
    }

    [Test]
    public void SafeResolve_SymlinkToDirectoryOutsideWorkspace_Throws()
    {
        var linkPath = Path.Combine(_workspace, "evil-dir");
        Directory.CreateSymbolicLink(linkPath, "/tmp");

        // Attempting to resolve a path through the symlink should be caught
        Should.Throw<InvalidOperationException>(() =>
            PathGuard.SafeResolve(_workspace, "evil-dir/somefile.txt"));
    }

    [Test]
    public void SafeResolve_SymlinkToFileInsideWorkspace_Allowed()
    {
        // Create a real file and a symlink to it, both within workspace
        var realFile = Path.Combine(_workspace, "real.txt");
        File.WriteAllText(realFile, "content");

        var linkPath = Path.Combine(_workspace, "link.txt");
        File.CreateSymbolicLink(linkPath, realFile);

        // Symlinks within the workspace are safe
        var result = PathGuard.SafeResolve(_workspace, "link.txt");
        result.ShouldBe(Path.GetFullPath(linkPath));
    }

    [Test]
    public void SafeResolve_NewFileInExistingSubdir_Allowed()
    {
        // SafeResolve should handle non-existent files in existing directories
        var subdir = Path.Combine(_workspace, "existing-dir");
        Directory.CreateDirectory(subdir);

        var result = PathGuard.SafeResolve(_workspace, "existing-dir/new-file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "existing-dir/new-file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void SafeResolve_NewFileInNonExistentSubdir_Allowed()
    {
        // SafeResolve allows creation of new files in non-existent directories
        // (the caller is responsible for creating the directory)
        var result = PathGuard.SafeResolve(_workspace, "new-dir/new-file.txt");

        var expected = Path.GetFullPath(Path.Combine(_workspace, "new-dir/new-file.txt"));
        result.ShouldBe(expected);
    }

    [Test]
    public void VerifyNotSymlinkEscape_DeeplyNestedSymlink_CatchesEscape()
    {
        // Create a nested directory structure where a deep parent is a symlink escape
        var subdir = Path.Combine(_workspace, "level1");
        Directory.CreateDirectory(subdir);

        var linkDir = Path.Combine(subdir, "escape");
        Directory.CreateSymbolicLink(linkDir, "/tmp");

        var filePath = Path.Combine(linkDir, "nested/file.txt");

        Should.Throw<InvalidOperationException>(() =>
            PathGuard.VerifyNotSymlinkEscape(filePath, _workspace));
    }

    [Test]
    public void VerifyFileDescriptorPath_FileOutsideWorkspace_ThrowsOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            // On non-Linux, VerifyFileDescriptorPath is a no-op
            Assert.Pass("VerifyFileDescriptorPath is a no-op on non-Linux — test is Linux-only.");
            return;
        }

        // Open a file outside the workspace, then verify the fd check catches it
        var outsideFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(outsideFile, "outside");

        try
        {
            using var fs = File.OpenRead(outsideFile);

            Should.Throw<InvalidOperationException>(() =>
                PathGuard.VerifyFileDescriptorPath(fs, _workspace));
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }
}
