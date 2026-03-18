using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Security;

/// <summary>
///     Manages one-time pairing codes and hashed bearer token authentication for the Web channel.
///     The plaintext token is issued exactly once; only its SHA-256 hash is stored on disk.
///     Brute-force protection: 5 failed attempts per IP triggers a 5-minute lockout.
/// </summary>
internal sealed partial class WebPairingGuard
{
    private const int MaxFailedAttempts = 5;

    private const int MaxFailureTrackingEntries = 10_000;

    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<IPAddress, (int Count, DateTimeOffset LockedUntil)> _failures = new();

    private readonly HashSet<string> _hashes = [];

    private readonly Lock _lock = new();

    private readonly ILogger _logger;

    private readonly string _persistPath;

    private string? _pairingCode;

    public WebPairingGuard(string persistPath, ILogger logger)
    {
        _logger = logger;
        _persistPath = persistPath;
        LoadFromDisk();
        if (_hashes.Count == 0)
        {
            _pairingCode = NewCode();
        }
    }

    /// <summary>Current one-time pairing code. Null after first use or if tokens already exist.</summary>
    public string? PairingCode
    {
        get
        {
            lock (_lock)
            {
                return _pairingCode;
            }
        }
    }

    /// <summary>True once at least one client has successfully paired.</summary>
    public bool HasPairedClients
    {
        get
        {
            lock (_lock)
            {
                return _hashes.Count > 0;
            }
        }
    }

    /// <summary>Returns true when the bearer token matches a stored SHA-256 hash (constant-time).</summary>
    public bool IsAuthenticated(string token)
    {
        var submittedBytes = Encoding.UTF8.GetBytes(HashToken(token));
        lock (_lock)
        {
            foreach (var stored in _hashes)
            {
                if (CryptographicOperations.FixedTimeEquals(submittedBytes, Encoding.UTF8.GetBytes(stored)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    ///     Attempts pairing with the supplied code from the given client IP.
    ///     Returns the new bearer token on success; null on wrong code, lockout, or null IP.
    /// </summary>
    public string? TryPair(IPAddress? clientIp, string code)
    {
        // No IP = can't pair (reject unknown clients)
        if (clientIp is null)
        {
            return null;
        }

        if (IsLockedOut(clientIp))
        {
            return null;
        }

        string token;
        lock (_lock)
        {
            if (_pairingCode is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(code.Trim()),
                    Encoding.UTF8.GetBytes(_pairingCode)))
            {
                RecordFailure(clientIp);
                return null;
            }

            _pairingCode = null; // one-time use — consumed
            token = GenerateToken();
            _hashes.Add(HashToken(token));
        }

        SaveToDisk();
        return token;
    }

    /// <summary>Generates a fresh pairing code, replacing any existing one. Returns the new code.</summary>
    public string RegenerateCode()
    {
        lock (_lock)
        {
            _pairingCode = NewCode();
            return _pairingCode;
        }
    }

    // ── private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Evict expired lockout entries when the dictionary grows beyond 10,000 entries
    /// to prevent unbounded memory growth from distributed brute-force attacks.
    /// </summary>
    private void EvictExpiredEntries()
    {
        if (_failures.Count <= MaxFailureTrackingEntries)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _failures)
        {
            if (kvp.Value.LockedUntil < now && kvp.Value.Count < MaxFailedAttempts)
            {
                _failures.TryRemove(kvp.Key, out _);
            }
        }
    }

    private bool IsLockedOut(IPAddress ip)
    {
        EvictExpiredEntries();

        if (!_failures.TryGetValue(ip, out var s) || s.Count < MaxFailedAttempts)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= s.LockedUntil)
        {
            // Lockout expired — reset counter so the IP is not permanently blocked.
            _failures[ip] = (0, default);
            return false;
        }

        return true;
    }

    private void RecordFailure(IPAddress ip)
    {
        _failures.AddOrUpdate(ip,
            _ => (1, DateTimeOffset.UtcNow + LockoutDuration),
            (_, old) =>
            {
                var c = old.Count + 1;
                var until = old.LockedUntil;
                if (c >= MaxFailedAttempts)
                {
                    until = DateTimeOffset.UtcNow + LockoutDuration;
                }

                return (c, until);
            });
    }

    // RandomNumberGenerator.GetInt32 uses rejection sampling internally — no modulo bias.
    private static string NewCode()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    private static string GenerateToken()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        return "cs_" + Convert.ToHexStringLower(buf);
    }

    private static string HashToken(string token)
    {
        var input = Encoding.UTF8.GetBytes(token);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_persistPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistPath);
            var hashes = JsonSerializer.Deserialize(json, WebPairingGuardJsonContext.Default.ListString) ?? [];
            lock (_lock)
            {
                foreach (var h in hashes)
                {
                    _hashes.Add(h);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable file — start fresh, but log so the operator knows
            LogCorruptTokenFile(_logger, _persistPath, ex.Message);
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_persistPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            List<string> snapshot;
            lock (_lock)
            {
                snapshot = [.._hashes];
            }

            File.WriteAllText(_persistPath, JsonSerializer.Serialize(snapshot, WebPairingGuardJsonContext.Default.ListString));
        }
        catch (Exception ex)
        {
            LogPersistTokensFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Failed to persist paired tokens")]
    private static partial void LogPersistTokensFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Corrupt or unreadable token file at '{FilePath}', starting fresh: {Reason}")]
    private static partial void LogCorruptTokenFile(ILogger logger, string filePath, string reason);
}

[JsonSerializable(typeof(List<string>))]
internal sealed partial class WebPairingGuardJsonContext : JsonSerializerContext;