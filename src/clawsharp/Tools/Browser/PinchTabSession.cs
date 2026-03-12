using System.Collections.Concurrent;

namespace Clawsharp.Tools.Browser;

/// <summary>
/// Tracks the active PinchTab tab ID per session (channel:senderId).
/// </summary>
public sealed class PinchTabSessionManager
{
    private readonly ConcurrentDictionary<string, string> _tabs = new(StringComparer.Ordinal);

    /// <summary>Gets the active tab ID for the given session, or null if none.</summary>
    public string? GetTabId(string sessionId) =>
        _tabs.TryGetValue(sessionId, out var tabId) ? tabId : null;

    /// <summary>Sets (or overwrites) the active tab ID for the given session.</summary>
    public void SetTabId(string sessionId, string tabId) =>
        _tabs[sessionId] = tabId;

    /// <summary>Removes the active tab ID for the given session.</summary>
    public void RemoveTabId(string sessionId) =>
        _tabs.TryRemove(sessionId, out _);
}