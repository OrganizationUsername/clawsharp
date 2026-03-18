namespace Clawsharp.Config.Search;

/// <summary>Perplexity AI search API configuration.</summary>
public sealed class PerplexityConfig
{
    /// <summary>Perplexity API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Perplexity model to use (default: sonar-pro).</summary>
    public string Model { get; init; } = "sonar-pro";
}