namespace Clawsharp.Config.Search;

/// <summary>Firecrawl search API configuration.</summary>
public sealed class FirecrawlConfig
{
    /// <summary>Firecrawl API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Firecrawl API base URL (default: https://api.firecrawl.dev).</summary>
    public string BaseUrl { get; init; } = "https://api.firecrawl.dev";
}