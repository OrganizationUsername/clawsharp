namespace Clawsharp.Config.Features;

/// <summary>
/// Controls whether prompt caching annotations are sent to LLM providers.
/// Defaults to fully enabled — set <see cref="Enabled"/> to false to suppress all caching.
/// </summary>
public sealed class CachingConfig
{
    /// <summary>
    /// Whether prompt caching is enabled (default: true).
    /// When false, the system-prompt static/dynamic split is suppressed and
    /// <c>cache_control</c> annotations are not sent to any provider.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether to mark the last tool definition with <c>cache_control</c> for Anthropic (default: true).
    /// Only evaluated when <see cref="Enabled"/> is true.
    /// Set to false when the tool set changes every turn (e.g. MCP servers restart frequently).
    /// </summary>
    public bool CacheToolDefinitions { get; init; } = true;
}