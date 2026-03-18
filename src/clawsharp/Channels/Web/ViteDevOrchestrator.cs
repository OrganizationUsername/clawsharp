using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Channels.Web;

/// <summary>
///     Manages the Vite dev server lifecycle. Auto-starts <c>npm run dev</c> in the
///     <c>clawsharp-web</c> directory, waits until the dev server is ready, streams
///     logs, detects already-running instances, and gracefully shuts down on host stop.
///     Only active when <c>DOTNET_ENVIRONMENT=Development</c>.
/// </summary>
public sealed partial class ViteDevOrchestrator(IHostEnvironment env, ILogger<ViteDevOrchestrator> logger) : IHostedLifecycleService
{
    private Process? _viteProcess;

    private const int VitePort = 5173;

    private const int VitePollIntervalMs = 300;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!env.IsDevelopment())
        {
            return;
        }

        if (await IsPortOpenAsync())
        {
            LogViteAlreadyRunning(logger);
            return;
        }

        var webDir = FindWebDir();
        if (webDir is null)
        {
            LogWebDirNotFound(logger);
            return;
        }

        LogStartingVite(logger);

        _viteProcess = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "npm.cmd" : "npm",
            Arguments = "run dev",
            WorkingDirectory = webDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (_viteProcess is null)
        {
            LogViteStartFailed(logger);
            return;
        }

        _viteProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                LogViteOutput(logger, e.Data);
            }
        };
        _viteProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                LogViteError(logger, e.Data);
            }
        };
        _viteProcess.BeginOutputReadLine();
        _viteProcess.BeginErrorReadLine();

        while (!await IsPortOpenAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(VitePollIntervalMs, cancellationToken);
        }

        LogViteReady(logger, VitePort);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        if (_viteProcess is { HasExited: false })
        {
            LogStoppingVite(logger);
            try
            {
                _viteProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                LogViteKillFailed(logger, ex);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<bool> IsPortOpenAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", VitePort);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Locate the <c>clawsharp-web</c> directory by searching relative to the assembly location.
    ///     In dev (<c>dotnet run</c>), <see cref="AppContext.BaseDirectory"/> is <c>bin/Debug/net10.0/</c>,
    ///     so we walk up to the project directory and then over to <c>clawsharp-web</c>.
    /// </summary>
    private static string? FindWebDir()
    {
        // bin/Debug/net10.0/ → project root → sibling clawsharp-web
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var candidate = Path.Combine(projectDir, "..", "clawsharp-web");
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "package.json")))
        {
            return Path.GetFullPath(candidate);
        }

        // Fallback: CWD-relative
        var cwdCandidate = Path.GetFullPath("clawsharp-web");
        if (Directory.Exists(cwdCandidate) && File.Exists(Path.Combine(cwdCandidate, "package.json")))
        {
            return cwdCandidate;
        }

        return null;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Vite dev server already running on port 5173")]
    private static partial void LogViteAlreadyRunning(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Starting Vite dev server...")]
    private static partial void LogStartingVite(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Vite ready at http://localhost:{Port}")]
    private static partial void LogViteReady(ILogger logger, int port);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Stopping Vite dev server...")]
    private static partial void LogStoppingVite(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "[vite] {Line}")]
    private static partial void LogViteOutput(ILogger logger, string line);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "[vite] {Line}")]
    private static partial void LogViteError(ILogger logger, string line);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "clawsharp-web directory not found — Vite dev server will not start")]
    private static partial void LogWebDirNotFound(ILogger logger);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Failed to start Vite process")]
    private static partial void LogViteStartFailed(ILogger logger);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Failed to kill Vite process")]
    private static partial void LogViteKillFailed(ILogger logger, Exception exception);
}