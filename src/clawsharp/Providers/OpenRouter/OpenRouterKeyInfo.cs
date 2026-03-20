using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>Response from GET /api/v1/key — API key info and credit balance.</summary>
internal sealed class OpenRouterKeyInfoResponse
{
    /// <summary>The API key details, or null if the key is invalid.</summary>
    [JsonPropertyName("data")]
    public OpenRouterKeyInfo? Data { get; init; }
}

/// <summary>API key metadata including credit balance and usage statistics.</summary>
internal sealed class OpenRouterKeyInfo
{
    /// <summary>Human-readable label assigned to this API key in the OpenRouter dashboard.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>Total credit limit in USD, or null for unlimited keys.</summary>
    [JsonPropertyName("limit")]
    public decimal? Limit { get; init; }

    /// <summary>Remaining credit balance in USD, or null for unlimited keys.</summary>
    [JsonPropertyName("limit_remaining")]
    public decimal? LimitRemaining { get; init; }

    /// <summary>Cumulative usage in USD across all time.</summary>
    [JsonPropertyName("usage")]
    public decimal Usage { get; init; }

    /// <summary>Usage in USD for the current day (UTC).</summary>
    [JsonPropertyName("usage_daily")]
    public decimal UsageDaily { get; init; }

    /// <summary>Usage in USD for the current calendar month.</summary>
    [JsonPropertyName("usage_monthly")]
    public decimal UsageMonthly { get; init; }

    /// <summary>Whether this key is on the free tier (rate-limited, no credits required).</summary>
    [JsonPropertyName("is_free_tier")]
    public bool IsFreeTier { get; init; }
}
