using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Memory.Sqlite;

/// <summary>
/// EF Core connection interceptor that loads the sqlite-vec extension (vec0)
/// on every opened connection. If the native binary is not found or loading fails,
/// the interceptor sets <see cref="VecExtensionLoaded"/> to <c>false</c> and all
/// ANN queries fall back to the existing FTS5 + in-process cosine path.
/// </summary>
internal sealed partial class SqliteVecConnectionInterceptor : DbConnectionInterceptor
{
    private static readonly string? ExtensionPath = FindExtensionPath();

    private static volatile bool _logged;

    /// <summary>
    /// Indicates whether the sqlite-vec extension was successfully loaded on the most recent connection.
    /// Thread-safe: set once per application lifetime after the first connection attempt.
    /// </summary>
    public static volatile bool VecExtensionLoaded;

    private readonly ILogger? _logger;

    public SqliteVecConnectionInterceptor(ILogger? logger = null)
    {
        _logger = logger;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        TryLoadVec((SqliteConnection)connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        TryLoadVec((SqliteConnection)connection);
        return Task.CompletedTask;
    }

    private void TryLoadVec(SqliteConnection conn)
    {
        if (ExtensionPath is null)
        {
            if (!_logged)
            {
                _logged = true;
                if (_logger is not null) LogVecBinaryNotFound(_logger);
            }

            return;
        }

        try
        {
            conn.LoadExtension(ExtensionPath, "sqlite3_vec_init");
            VecExtensionLoaded = true;
        }
        catch (Exception ex)
        {
            if (!_logged)
            {
                _logged = true;
                if (_logger is not null) LogVecLoadFailed(_logger, ex, ExtensionPath!);
            }
        }
    }

    private static string? FindExtensionPath()
    {
        var rid = GetRid();
        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "vec0.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "vec0.dylib"
            : "vec0.so";

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", ext),
            Path.Combine(AppContext.BaseDirectory, ext),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "win-x64" : "win-arm64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "sqlite-vec native binary not found; ANN search disabled, falling back to FTS5 + in-process cosine")]
    private static partial void LogVecBinaryNotFound(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Failed to load sqlite-vec extension from {Path}; ANN search disabled, falling back to FTS5 + in-process cosine")]
    private static partial void LogVecLoadFailed(ILogger logger, Exception exception, string path);
}