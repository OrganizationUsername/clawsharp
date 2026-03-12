using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Core.Sessions;

public sealed partial class SessionManager
{
    private readonly string _dir;

    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
        var root = Config.ConfigLoader.ExpandHome("~/.clawsharp");
        Directory.CreateDirectory(root);
        _dir = Path.Combine(root, "sessions");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Test-only constructor with custom sessions directory.</summary>
    internal SessionManager(string sessionsDir, ILogger<SessionManager>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionManager>.Instance;
        Directory.CreateDirectory(sessionsDir);
        _dir = sessionsDir;
    }

    public async Task<Session> LoadOrCreateAsync(string sessionId, CancellationToken ct = default)
    {
        var path = SessionPath(sessionId);
        if (!File.Exists(path))
        {
            return new Session { Id = sessionId };
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var session = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Default.Session, ct);
            return session ?? new Session { Id = sessionId };
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LogSessionLoadFailed(_logger, ex, path);
            return new Session { Id = sessionId };
        }
    }

    public async Task SaveAsync(Session session, CancellationToken ct = default)
    {
        var path = SessionPath(session.Id);
        var tmp = path + ".tmp";
        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, session, SessionJsonContext.Default.Session, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(tmp, path, true);
        }
        catch
        {
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                /* best-effort cleanup */
            }

            throw;
        }
    }

    /// <summary>
    /// Builds a safe filesystem path for the given session ID.
    /// Uses <see cref="Uri.EscapeDataString"/> for reversible, collision-free encoding.
    /// Falls back to a truncated SHA-256 hash if the encoded name exceeds 200 characters.
    /// </summary>
    internal string SessionPath(string sessionId)
    {
        var encoded = Uri.EscapeDataString(sessionId);

        if (encoded.Length > 200)
        {
            encoded = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..16];
        }

        return Path.Combine(_dir, encoded + ".json");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to load session {Path}, starting fresh")]
    private static partial void LogSessionLoadFailed(ILogger logger, Exception exception, string path);
}