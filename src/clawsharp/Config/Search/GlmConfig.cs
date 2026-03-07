namespace Clawsharp.Config.Search;

/// <summary>GLM/Zhipu AI search API configuration (Chinese web search via chat completions).</summary>
public sealed class GlmConfig
{
    /// <summary>
    /// GLM API key in the format <c>{id}.{secret}</c>. The id and secret are split on
    /// the first dot and used to generate short-lived JWT bearer tokens.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>GLM model to use for web search. Default: web-search-pro.</summary>
    public string Model { get; set; } = "web-search-pro";
}