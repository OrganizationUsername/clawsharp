using System.Text.Json;
using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Security;

namespace Clawsharp.Security;

/// <summary>
/// Append-only JSONL audit logger with size-based rotation and 90-day retention.
/// Thread-safe via SemaphoreSlim.
/// </summary>
public sealed partial class AuditLogger : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly AuditConfig _config;

    private readonly ILogger<AuditLogger> _logger;

    private readonly string _logPath;

    public AuditLogger(IOptions<AppConfig> options, ILogger<AuditLogger> logger)
    {
        _config = options.Value.Audit ?? new AuditConfig();
        _logger = logger;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".clawsharp");
        Directory.CreateDirectory(dir);

        var defaultLogPath = Path.Combine(dir, "audit.log");

        if (string.IsNullOrWhiteSpace(_config.LogPath))
        {
            _logPath = defaultLogPath;
        }
        else
        {
            var configuredPath = Path.IsPathRooted(_config.LogPath)
                ? _config.LogPath
                : Path.Combine(dir, _config.LogPath);

            // MED-07: Validate that the resolved audit log path stays within ~/.clawsharp/
            // to prevent an attacker with config write access from redirecting audit logs
            // to arbitrary locations (e.g. overwriting system files or hiding audit trails).
            var resolvedLogPath = Path.GetFullPath(configuredPath);
            var clawsharpDir = Path.GetFullPath(dir);
            if (resolvedLogPath.StartsWith(clawsharpDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(resolvedLogPath, clawsharpDir, StringComparison.Ordinal))
            {
                _logPath = resolvedLogPath;
            }
            else
            {
                LogAuditPathOutsideBase(logger, configuredPath);
                _logPath = defaultLogPath;
            }
        }

        if (_config.Enabled)
        {
            PruneOldLogs();
        }
    }

    public async Task LogAsync(AuditEvent evt, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return;
        }

        // Internal try-catch ensures fire-and-forget callers (_ = LogXxxAsync(...)) are safe —
        // audit logging must never crash the caller or surface unobserved task exceptions.
        try
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(evt, AuditJsonContext.Default.AuditEvent);

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RotateIfNeededAsync().ConfigureAwait(false);
                await using var fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                await fs.WriteAsync(jsonBytes, ct).ConfigureAwait(false);
                fs.WriteByte((byte)'\n');
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWriteAuditEventFailed(_logger, ex);
        }
    }

    /// <summary>Convenience: log a command execution event.</summary>
    public Task LogCommandAsync(
        string command,
        string? channel,
        string? userId,
        bool allowed,
        bool success,
        int? exitCode = null,
        long? durationMs = null,
        string? error = null,
        string? sandboxBackend = null,
        CancellationToken ct = default)
        => LogAsync(new AuditEvent
        {
            EventType = AuditEventTypes.CommandExecution,
            Actor = new AuditActor { Channel = channel, UserId = userId },
            Action = new AuditAction { Command = command, Allowed = allowed },
            Result = new AuditResult { Success = success, ExitCode = exitCode, DurationMs = durationMs, Error = error },
            Security = sandboxBackend is not null ? new AuditSecurity { SandboxBackend = sandboxBackend } : null,
        }, ct);

    /// <summary>Convenience: log a policy violation (SSRF block, ShellGuard deny, etc.).</summary>
    public Task LogPolicyViolationAsync(
        string detail,
        string? channel = null,
        string? userId = null,
        bool ssrfBlocked = false,
        CancellationToken ct = default)
        => LogAsync(new AuditEvent
        {
            EventType = AuditEventTypes.PolicyViolation,
            Actor = new AuditActor { Channel = channel, UserId = userId },
            Action = new AuditAction { Detail = detail, Allowed = false },
            Security = new AuditSecurity { PolicyViolation = true, SsrfBlocked = ssrfBlocked, BlockedReason = detail },
        }, ct);

    /// <summary>Convenience: log a security event (injection detected, etc.).</summary>
    public Task LogSecurityEventAsync(
        string detail,
        string? channel = null,
        string? userId = null,
        CancellationToken ct = default)
        => LogAsync(new AuditEvent
        {
            EventType = AuditEventTypes.SecurityEvent,
            Actor = new AuditActor { Channel = channel, UserId = userId },
            Action = new AuditAction { Detail = detail, Allowed = false },
        }, ct);

    /// <summary>Convenience: log a file access (read, write, git) event.</summary>
    public Task LogFileAccessAsync(
        string path,
        string operation,
        string? channel = null,
        bool success = true,
        string? error = null,
        CancellationToken ct = default)
        => LogAsync(new AuditEvent
        {
            EventType = AuditEventTypes.FileAccess,
            Actor = new AuditActor { Channel = channel },
            Action = new AuditAction { Command = $"{operation}:{path}", Allowed = true },
            Result = new AuditResult { Success = success, Error = error },
        }, ct);

    private async Task RotateIfNeededAsync()
    {
        if (!File.Exists(_logPath))
        {
            return;
        }

        var info = new FileInfo(_logPath);
        long maxBytes = (long)_config.MaxSizeMb * 1024 * 1024;
        if (info.Length < maxBytes)
        {
            return;
        }

        // Rename audit.log.9 -> delete, .8 -> .9, ..., .1 -> .2, audit.log -> .1
        for (var i = 9; i >= 1; i--)
        {
            var src = $"{_logPath}.{i}";
            var dst = $"{_logPath}.{i + 1}";
            if (File.Exists(src))
            {
                if (i == 9)
                {
                    File.Delete(src);
                }
                else
                {
                    File.Move(src, dst, overwrite: true);
                }
            }
        }

        File.Move(_logPath, $"{_logPath}.1", overwrite: true);
        LogAuditLogRotated(_logger, _logPath);
    }

    private void PruneOldLogs()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays);
        var dir = Path.GetDirectoryName(_logPath)!;
        var baseName = Path.GetFileName(_logPath);

        try
        {
            foreach (var file in Directory.GetFiles(dir, $"{baseName}.*"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                    LogAuditLogPruned(_logger, file);
                }
            }
        }
        catch (Exception ex)
        {
            LogPruneAuditLogsFailed(_logger, ex);
        }
    }

    public void Dispose() => _lock.Dispose();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to write audit event")]
    private static partial void LogWriteAuditEventFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Audit log rotated to {Path}.1")]
    private static partial void LogAuditLogRotated(ILogger logger, string path);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Pruned old audit log: {File}")]
    private static partial void LogAuditLogPruned(ILogger logger, string file);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to prune old audit logs")]
    private static partial void LogPruneAuditLogsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Audit log path '{Path}' resolves outside ~/.clawsharp/ — using default path")]
    private static partial void LogAuditPathOutsideBase(ILogger logger, string path);
}