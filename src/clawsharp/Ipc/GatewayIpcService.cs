using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawsharp.Core.Services;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Ipc;

/// <summary>
///     Named-pipe IPC server that runs inside the gateway process.
///     Allows CLI sub-commands (e.g. <c>clawsharp channel pair-web</c>) to communicate
///     with the running gateway and receive an immediate response instead of polling a sentinel file.
/// </summary>
/// <remarks>
///     Defense-in-depth authentication: on startup, a random token is written to
///     <c>~/.clawsharp/ipc.token</c> with owner-only permissions. Clients must include
///     the token in every <see cref="IpcRequest"/>. The token is deleted on shutdown.
/// </remarks>
public sealed partial class GatewayIpcService : LifecycleBackgroundService
{
    /// <summary>User-scoped pipe name -- avoids conflicts when multiple users share a machine.</summary>
    internal static string PipeName => $"clawsharp-{Environment.UserName}";

    /// <summary>Path to the IPC auth token file.</summary>
    internal static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clawsharp", "ipc.token");

    /// <summary>Maximum number of concurrent IPC connections.</summary>
    private const int MaxConcurrentConnections = 5;

    /// <summary>Grace period for active connections to finish during shutdown.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);

    private readonly WebPairingService _pairingService;

    private readonly ILogger<GatewayIpcService> _logger;

    private readonly SemaphoreSlim _connectionSemaphore = new(MaxConcurrentConnections, MaxConcurrentConnections);

    private string _authToken = "";

    private int _activeConnections;

    public GatewayIpcService(WebPairingService pairingService, ILogger<GatewayIpcService> logger)
    {
        _pairingService = pairingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _authToken = GenerateAndPersistToken();
        LogStarting(_logger, PipeName);

        try
        {
            await AcceptLoopAsync(stoppingToken);
        }
        finally
        {
            // Graceful drain: wait for active connections to finish (up to DrainTimeout).
            await DrainActiveConnectionsAsync();
            CleanupTokenFile();
        }
    }

    /// <summary>Generates a cryptographic random token, writes it to disk, and returns it.</summary>
    private string GenerateAndPersistToken()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        var token = Convert.ToHexStringLower(buf);

        var dir = Path.GetDirectoryName(TokenPath)!;
        Directory.CreateDirectory(dir);

        // Write to a temp file, set permissions, then atomically move into place.
        var tmpPath = TokenPath + ".tmp";
        File.WriteAllText(tmpPath, token);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(tmpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                LogTokenPermissionWarning(_logger, ex);
            }
        }

        File.Move(tmpPath, TokenPath, overwrite: true);
        return token;
    }

    /// <summary>Best-effort removal of the token file on shutdown.</summary>
    private void CleanupTokenFile()
    {
        try
        {
            if (File.Exists(TokenPath))
                File.Delete(TokenPath);
        }
        catch (Exception ex)
        {
            LogTokenCleanupWarning(_logger, ex);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync();
                LogAcceptError(_logger, ex);
                continue;
            }

            // Enforce connection limit -- if we can't acquire, reject immediately.
            if (!_connectionSemaphore.Wait(0))
            {
                LogConnectionLimitReached(_logger);
                await RejectAndDisposeAsync(pipe);
                continue;
            }

            Interlocked.Increment(ref _activeConnections);

            // Fire-and-forget -- HandleConnectionAsync owns the pipe and disposes it.
            _ = HandleConnectionAsync(pipe, stoppingToken);
        }
    }

    /// <summary>Sends an error response and disposes the pipe when at connection limit.</summary>
    private static async Task RejectAndDisposeAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                var resp = new IpcResponse(null, "too_many_connections", false);
                var json = JsonSerializer.Serialize(resp, IpcJsonContext.Default.IpcResponse);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await writer.WriteLineAsync(json.AsMemory(), cts.Token);
            }
            catch
            {
                // Best effort -- discard.
            }
        }
    }

    /// <summary>Waits for in-flight connections to complete before returning.</summary>
    private async Task DrainActiveConnectionsAsync()
    {
        if (Volatile.Read(ref _activeConnections) <= 0)
            return;

        LogDraining(_logger, Volatile.Read(ref _activeConnections));

        using var drainCts = new CancellationTokenSource(DrainTimeout);
        try
        {
            while (Volatile.Read(ref _activeConnections) > 0 && !drainCts.IsCancellationRequested)
            {
                await Task.Delay(50, drainCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            LogDrainTimeout(_logger, Volatile.Read(ref _activeConnections));
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            await using (pipe)
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                    var line = await reader.ReadLineAsync(timeoutCts.Token);
                    if (line is null) return;

                    IpcRequest? req;
                    try
                    {
                        req = JsonSerializer.Deserialize(line, IpcJsonContext.Default.IpcRequest);
                    }
                    catch
                    {
                        req = null;
                    }

                    IpcResponse resp;
                    if (req is null)
                    {
                        resp = new IpcResponse(null, "invalid_request", false);
                    }
                    else if (!ValidateToken(req.Token))
                    {
                        LogAuthFailure(_logger);
                        resp = new IpcResponse(null, "auth_failed", false);
                    }
                    else
                    {
                        resp = req.Command switch
                        {
                            "pair_web" => PairWeb(),
                            _ => new IpcResponse(null, "unknown_command", false)
                        };
                    }

                    var json = JsonSerializer.Serialize(resp, IpcJsonContext.Default.IpcResponse);
                    await writer.WriteLineAsync(json.AsMemory(), timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected or per-connection timeout -- nothing to do.
                }
                catch (Exception ex)
                {
                    LogConnectionError(_logger, ex);
                }
            }
        }
        finally
        {
            _connectionSemaphore.Release();
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    /// <summary>
    ///     Validates the client-supplied token against the server's auth token
    ///     using constant-time comparison to prevent timing side-channels.
    /// </summary>
    private bool ValidateToken(string? clientToken)
    {
        if (string.IsNullOrEmpty(clientToken) || string.IsNullOrEmpty(_authToken))
            return false;

        var provided = Encoding.UTF8.GetBytes(clientToken);
        var expected = Encoding.UTF8.GetBytes(_authToken);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private IpcResponse PairWeb()
    {
        if (!_pairingService.IsEnabled)
            return new IpcResponse(null, "web_pairing_not_enabled", false);

        var code = _pairingService.Regenerate();
        return new IpcResponse(code, null, false);
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "IPC server listening on pipe: {PipeName}")]
    private static partial void LogStarting(ILogger logger, string pipeName);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "IPC server error accepting connection")]
    private static partial void LogAcceptError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "IPC connection handler error")]
    private static partial void LogConnectionError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "IPC auth token file permission could not be set")]
    private static partial void LogTokenPermissionWarning(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "IPC auth token file cleanup failed")]
    private static partial void LogTokenCleanupWarning(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "IPC connection rejected: max concurrent connections reached")]
    private static partial void LogConnectionLimitReached(ILogger logger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "IPC authentication failed -- invalid or missing token")]
    private static partial void LogAuthFailure(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "IPC server draining {Count} active connection(s)")]
    private static partial void LogDraining(ILogger logger, int count);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "IPC drain timeout -- {Count} connection(s) still active")]
    private static partial void LogDrainTimeout(ILogger logger, int count);
}