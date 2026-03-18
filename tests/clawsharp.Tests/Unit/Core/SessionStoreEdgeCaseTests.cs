using Clawsharp.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Unit.Core;

/// <summary>
/// Edge-case tests for <see cref="SessionStore"/>:
/// path traversal in session IDs, malformed JSON, and boundary conditions.
/// </summary>
[TestFixture]
public sealed class SessionStoreEdgeCaseTests : IDisposable
{
    private readonly string _sessionsDir;

    public SessionStoreEdgeCaseTests()
    {
        _sessionsDir = Path.Combine(Path.GetTempPath(), $"clawsharp-session-edge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_sessionsDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private SessionStore CreateStore() => new(_sessionsDir, NullLogger<SessionStore>.Instance);

    // ── HIGH-8: Session ID path traversal ───────────────────────────────

    [Test]
    public void SessionPath_PathTraversal_DotsAreEncoded()
    {
        var store = CreateStore();

        var path = store.SessionPath("../../etc/passwd");

        // Uri.EscapeDataString encodes dots and slashes, so ".." becomes "%2E%2E"
        // and "/" becomes "%2F". The result must be within the sessions directory.
        var fullPath = Path.GetFullPath(path);
        // Path traversal in session ID must not escape the sessions directory.
        fullPath.ShouldStartWith(Path.GetFullPath(_sessionsDir));
    }

    [Test]
    public void SessionPath_PathTraversal_SlashesAreEncoded()
    {
        var store = CreateStore();

        var path = store.SessionPath("../../../root/.ssh/authorized_keys");

        var fullPath = Path.GetFullPath(path);
        // Forward-slash traversal must be encoded.
        fullPath.ShouldStartWith(Path.GetFullPath(_sessionsDir));
    }

    [Test]
    public void SessionPath_BackslashTraversal_EncodedSafely()
    {
        var store = CreateStore();

        var path = store.SessionPath(@"..\..\etc\passwd");

        var fullPath = Path.GetFullPath(path);
        // Backslash traversal must be encoded safely.
        fullPath.ShouldStartWith(Path.GetFullPath(_sessionsDir));
    }

    [Test]
    public void SessionPath_NullByteInjection_EncodedSafely()
    {
        var store = CreateStore();

        // Null bytes can truncate filenames on some systems
        var path = store.SessionPath("session\0.json");

        var fullPath = Path.GetFullPath(path);
        // Null byte injection must not escape sessions directory.
        fullPath.ShouldStartWith(Path.GetFullPath(_sessionsDir));
    }

    [Test]
    public void SessionPath_ColonInSessionId_EncodedSafely()
    {
        // Normal session IDs use "channel:senderId" format with colon
        var store = CreateStore();

        var path = store.SessionPath("telegram:user123");

        var fullPath = Path.GetFullPath(path);
        fullPath.ShouldStartWith(Path.GetFullPath(_sessionsDir));
        // The encoded colon "%3A" should be present in the filename
        Path.GetFileName(path).ShouldContain("%3A");
    }

    [Test]
    public void SessionPath_VeryLongSessionId_FallsBackToHash()
    {
        var store = CreateStore();

        // Create a session ID that, when encoded, exceeds 200 characters
        var longId = new string('A', 300);
        var path = store.SessionPath(longId);

        // The filename should be a truncated SHA-256 hash (16 hex chars) + ".json"
        var filename = Path.GetFileName(path);
        filename.ShouldEndWith(".json");
        // 16 hex chars + ".json" = 21 chars
        filename.Length.ShouldBe(21,
            "Long session IDs should fall back to 16-char SHA-256 hash");
    }

    [Test]
    public void SessionPath_DifferentLongIds_ProduceDifferentHashes()
    {
        var store = CreateStore();

        var longId1 = new string('A', 300);
        var longId2 = new string('B', 300);

        var path1 = store.SessionPath(longId1);
        var path2 = store.SessionPath(longId2);

        path1.ShouldNotBe(path2, "Different long session IDs must hash to different filenames");
    }

    // ── MED-28: Malformed session JSON returns fresh session ────────────

    [Test]
    public async Task LoadOrCreateAsync_CorruptedJson_ReturnsFreshSession()
    {
        var store = CreateStore();

        // Write corrupted JSON to the session file
        var sessionId = "test:corrupt";
        var path = store.SessionPath(sessionId);
        await File.WriteAllTextAsync(path, "{{{{not valid json at all!!!!");

        var session = await store.LoadOrCreateAsync(sessionId);

        session.ShouldNotBeNull();
        session.Id.ShouldBe(sessionId);
        session.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task LoadOrCreateAsync_EmptyFile_ReturnsFreshSession()
    {
        var store = CreateStore();

        var sessionId = "test:empty";
        var path = store.SessionPath(sessionId);
        await File.WriteAllTextAsync(path, "");

        var session = await store.LoadOrCreateAsync(sessionId);

        session.ShouldNotBeNull();
        session.Id.ShouldBe(sessionId);
        session.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task LoadOrCreateAsync_JsonNull_ReturnsFreshSession()
    {
        var store = CreateStore();

        var sessionId = "test:null";
        var path = store.SessionPath(sessionId);
        await File.WriteAllTextAsync(path, "null");

        var session = await store.LoadOrCreateAsync(sessionId);

        session.ShouldNotBeNull();
        session.Id.ShouldBe(sessionId);
        session.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task LoadOrCreateAsync_TruncatedJson_ReturnsFreshSession()
    {
        var store = CreateStore();

        var sessionId = "test:truncated";
        var path = store.SessionPath(sessionId);
        // Partial/truncated JSON object
        await File.WriteAllTextAsync(path, "{\"id\":\"test:truncated\",\"messages\":[{\"role\":");

        var session = await store.LoadOrCreateAsync(sessionId);

        session.ShouldNotBeNull();
        session.Id.ShouldBe(sessionId);
        session.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task LoadOrCreateAsync_WrongJsonStructure_ReturnsFreshSession()
    {
        var store = CreateStore();

        var sessionId = "test:wrongtype";
        var path = store.SessionPath(sessionId);
        // Valid JSON but wrong structure (array instead of object)
        await File.WriteAllTextAsync(path, "[1, 2, 3]");

        var session = await store.LoadOrCreateAsync(sessionId);

        session.ShouldNotBeNull();
        session.Id.ShouldBe(sessionId);
        session.Messages.ShouldBeEmpty();
    }

    // ── Round-trip correctness ──────────────────────────────────────────

    [Test]
    public async Task SaveAndLoad_RoundTrip_PreservesSessionData()
    {
        var store = CreateStore();
        var sessionId = "test:roundtrip";

        var session = new Session
        {
            Id = sessionId,
            TotalInputTokens = 42,
            TotalOutputTokens = 17
        };

        await store.SaveAsync(session);
        var loaded = await store.LoadOrCreateAsync(sessionId);

        loaded.Id.ShouldBe(sessionId);
        loaded.TotalInputTokens.ShouldBe(42);
        loaded.TotalOutputTokens.ShouldBe(17);
    }

    [Test]
    public async Task LoadOrCreateAsync_NonExistentSession_ReturnsFreshSession()
    {
        var store = CreateStore();

        var session = await store.LoadOrCreateAsync("test:doesnotexist");

        session.ShouldNotBeNull();
        session.Id.ShouldBe("test:doesnotexist");
        session.Messages.ShouldBeEmpty();
    }

    // ── Atomic save (tmp file cleanup) ──────────────────────────────────

    [Test]
    public async Task SaveAsync_NoLeftoverTmpFiles()
    {
        var store = CreateStore();

        var session = new Session { Id = "test:atomicsave" };
        await store.SaveAsync(session);

        // Verify no .tmp files remain
        var tmpFiles = Directory.GetFiles(_sessionsDir, "*.tmp");
        tmpFiles.ShouldBeEmpty("SaveAsync should clean up temp files after atomic rename");
    }
}
