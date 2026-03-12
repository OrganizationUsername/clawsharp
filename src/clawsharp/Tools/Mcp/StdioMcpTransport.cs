using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Clawsharp.Config.Features;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// MCP transport over stdio — launches a child process and communicates via
/// JSON-RPC over stdin/stdout (line-delimited).
/// </summary>
internal sealed partial class StdioMcpTransport : IMcpTransport
{
    private readonly Process _process;

    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly ILogger _logger;

    private readonly string _serverName;

    private int _nextId = 1;

    private StdioMcpTransport(string serverName, Process process, ILogger logger)
    {
        _serverName = serverName;
        _process = process;
        _logger = logger;
    }

    /// <summary>Whether the underlying process is still running.</summary>
    public bool IsConnected
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Exit code of the process (null if still running).</summary>
    public int? ExitCode
    {
        get
        {
            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Starts a child process with the given config and returns a ready transport.
    /// </summary>
    public static StdioMcpTransport Start(string serverName, McpServerConfig config, ILogger logger)
    {
        var psi = new ProcessStartInfo
        {
            FileName = config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };

        if (config.Args is { Count: > 0 })
        {
            foreach (var arg in config.Args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        if (config.Env is { Count: > 0 })
        {
            foreach (var (key, value) in config.Env)
            {
                psi.Environment[key] = value;
            }
        }

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to start MCP server '{serverName}': process returned null.");

        return new StdioMcpTransport(serverName, process, logger);
    }

    public async Task<McpResponse> SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureRunning();

            var id = _nextId++;
            var request = new McpRequest
            {
                Id = id,
                Method = method,
                Params = parameters
            };

            var json = JsonSerializer.Serialize(request, McpJsonContext.Default.McpRequest);
            LogOutbound(_logger, _serverName, json);

            await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);

            // Read lines until we get a JSON-RPC response with our id.
            // Skip notifications (lines with no "id" or id=null).
            while (true)
            {
                var line = await ReadLineWithTimeoutAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                if (line is null)
                {
                    throw new InvalidOperationException($"MCP server '{_serverName}' closed stdout unexpectedly.");
                }

                LogInbound(_logger, _serverName, line);

                McpResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize(line, McpJsonContext.Default.McpResponse);
                }
                catch (JsonException)
                {
                    // Not valid JSON-RPC -- skip (could be server log output)
                    continue;
                }

                if (response is null)
                {
                    continue;
                }

                // Skip notifications (no id)
                if (response.Id is null)
                {
                    continue;
                }

                // Match our request id
                if (response.Id == id)
                {
                    return response;
                }

                // Mismatched id -- log and continue
                LogIdMismatch(_logger, _serverName, id, response.Id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureRunning();

            var notification = new McpRequest
            {
                Id = null, // Notifications have no id
                Method = method,
                Params = parameters
            };

            var json = JsonSerializer.Serialize(notification, McpJsonContext.Default.McpRequest);
            LogOutboundNotification(_logger, _serverName, json);

            await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string?> ReadLineWithTimeoutAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP server '{_serverName}' did not respond within {timeout.TotalSeconds}s.");
        }
    }

    private void EnsureRunning()
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException(
                $"MCP server '{_serverName}' process has exited with code {_process.ExitCode}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();

                // Give the process a moment to exit gracefully
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex)
        {
            LogDisposeError(_logger, _serverName, ex);
        }
        finally
        {
            _process.Dispose();
            _lock.Dispose();
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Debug,
        Message = "MCP stdio [{ServerName}] --> {Json}")]
    private static partial void LogOutbound(ILogger logger, string serverName, string json);

    [LoggerMessage(EventId = 101, Level = LogLevel.Debug,
        Message = "MCP stdio [{ServerName}] <-- {Line}")]
    private static partial void LogInbound(ILogger logger, string serverName, string line);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
        Message = "MCP stdio server '{ServerName}' response id mismatch: expected {Expected}, got {Actual}")]
    private static partial void LogIdMismatch(ILogger logger, string serverName, int expected, int? actual);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug,
        Message = "MCP stdio [{ServerName}] --> (notification) {Json}")]
    private static partial void LogOutboundNotification(ILogger logger, string serverName, string json);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning,
        Message = "Error disposing MCP stdio transport '{ServerName}'")]
    private static partial void LogDisposeError(ILogger logger, string serverName, Exception exception);
}