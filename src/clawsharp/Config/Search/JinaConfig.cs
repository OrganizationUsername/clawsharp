namespace Clawsharp.Config.Search;

/// <summary>Jina search API configuration.</summary>
public sealed class JinaConfig
{
    /// <summary>Jina API key (optional; unauthenticated requests are rate-limited).</summary>
    public string? ApiKey { get; set; }
}