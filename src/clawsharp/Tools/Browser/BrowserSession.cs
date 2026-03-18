using System.Collections.Concurrent;
using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Clawsharp.Config.Features;

namespace Clawsharp.Tools.Browser;

/// <summary>
/// Manages one Playwright browser session: IPlaywright, IBrowser, IBrowserContext, and IPage.
/// Thread-safe via SemaphoreSlim. Disposes resources and optionally persists session state.
/// </summary>
public sealed partial class BrowserSession(string stateFilePath, bool headless, ILogger logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;

    private IBrowser? _browser;

    private IBrowserContext? _context;

    private IPage? _page;

    private bool _disposed;

    /// <summary>
    /// Returns the active page, lazily creating Playwright/Browser/Context/Page on first call.
    /// Thread-safe: only one caller can initialize or access at a time.
    /// </summary>
    public async Task<IPage> GetPageAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_page is not null && !_page.IsClosed)
            {
                return _page;
            }

            await InitializeAsync().ConfigureAwait(false);
            return _page!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
        }).ConfigureAwait(false);

        // Load saved session state (cookies, localStorage) if it exists.
        var contextOptions = new BrowserNewContextOptions();
        if (File.Exists(stateFilePath))
        {
            try
            {
                contextOptions.StorageStatePath = stateFilePath;
                LogStateRestored(logger, stateFilePath);
            }
            catch (Exception ex)
            {
                LogRestoreStateFailed(logger, ex, stateFilePath);
            }
        }

        _context = await _browser.NewContextAsync(contextOptions).ConfigureAwait(false);
        _page = await _context.NewPageAsync().ConfigureAwait(false);
    }

    /// <summary>Save browser context storage state (cookies, localStorage) to disk.</summary>
    public async Task SaveStateAsync()
    {
        if (_context is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(stateFilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            await _context.StorageStateAsync(new BrowserContextStorageStateOptions
            {
                Path = stateFilePath,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSaveStateFailed(logger, ex, stateFilePath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await SaveStateAsync().ConfigureAwait(false);

        if (_page is not null)
        {
            try
            {
                await _page.CloseAsync();
            }
            catch
            {
                /* best effort */
            }
        }

        if (_context is not null)
        {
            try
            {
                await _context.CloseAsync();
            }
            catch
            {
                /* best effort */
            }
        }

        if (_browser is not null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch
            {
                /* best effort */
            }
        }

        _playwright?.Dispose();
        _gate.Dispose();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Restored browser session state from {Path}")]
    private static partial void LogStateRestored(ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to restore browser session state from {Path}")]
    private static partial void LogRestoreStateFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to save browser session state to {Path}")]
    private static partial void LogSaveStateFailed(ILogger logger, Exception exception, string path);
}

/// <summary>
/// Singleton manager that creates and caches <see cref="BrowserSession"/> instances
/// keyed by session ID (typically "{channel}:{senderId}").
/// </summary>
public sealed partial class BrowserSessionCache(
    IOptions<AppConfig> configOptions,
    ILogger<BrowserSessionCache> logger)
    : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, BrowserSession> _sessions = new(StringComparer.Ordinal);

    private readonly BrowserConfig _config = configOptions.Value.Tools.Browser;

    /// <summary>
    /// Gets or creates a <see cref="BrowserSession"/> for the given session ID.
    /// </summary>
    public BrowserSession GetOrCreate(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, id =>
        {
            var sessionsDir = ConfigLoader.ExpandHome(_config.SessionsDir);
            Directory.CreateDirectory(sessionsDir);
            var stateFile = Path.Combine(sessionsDir, $"browser-{SanitizeSessionId(id)}.json");
            LogSessionCreating(logger, id, stateFile);
            return new BrowserSession(stateFile, _config.Headless, logger);
        });
    }

    /// <summary>Disposes and removes a specific session.</summary>
    public async Task CloseSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            LogSessionClosed(logger, sessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _sessions)
        {
            await kvp.Value.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
    }

    /// <summary>Sanitize session ID for safe use as a filename component.</summary>
    private static string SanitizeSessionId(string id) =>
        string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-'));

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Creating browser session {SessionId} with state file {Path}")]
    private static partial void LogSessionCreating(ILogger logger, string sessionId, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Closed browser session {SessionId}")]
    private static partial void LogSessionClosed(ILogger logger, string sessionId);
}