using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clawsharp.Config.Features;

/// <summary>
///     HTTP resilience pipeline configuration for provider calls.
///     Controls Polly retry, timeout, and circuit breaker behavior.
/// </summary>
public sealed class ResilienceConfig
{
    /// <summary>Retry policy configuration.</summary>
    public RetryPolicyConfig Retry { get; init; } = new();

    /// <summary>Request timeout for LLM provider calls.</summary>
    [JsonConverter(typeof(TimeSpanSecondsConverter))]
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Circuit breaker configuration. Null or absent disables the circuit breaker.</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }
}

/// <summary>Polly retry strategy options for transient HTTP errors (429, 503, network failures).</summary>
public sealed class RetryPolicyConfig
{
    /// <summary>Maximum number of retry attempts before giving up. Use -1 for infinite retries.</summary>
    [Range(-1, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Initial delay between retries.</summary>
    [JsonConverter(typeof(TimeSpanSecondsConverter))]
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay between retries (caps exponential growth).</summary>
    [JsonConverter(typeof(TimeSpanSecondsConverter))]
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Backoff strategy: "exponential", "linear", or "constant".</summary>
    public string BackoffType { get; init; } = "exponential";

    /// <summary>Add random jitter to retry delays to prevent thundering herd.</summary>
    public bool UseJitter { get; init; } = true;
}

/// <summary>
///     Circuit breaker configuration. Opens the circuit after consecutive failures,
///     preventing requests to a failing provider for a break duration.
/// </summary>
public sealed class CircuitBreakerConfig
{
    /// <summary>Whether the circuit breaker is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Failure ratio threshold (0.0–1.0) within the sampling window to trip the breaker.</summary>
    [Range(0.1, 1.0)]
    public double FailureRatio { get; init; } = 0.5;

    /// <summary>Minimum number of requests in the sampling window before the breaker can trip.</summary>
    [Range(2, 100)]
    public int MinimumThroughput { get; init; } = 5;

    /// <summary>Duration the circuit stays open before allowing a probe request.</summary>
    [JsonConverter(typeof(TimeSpanSecondsConverter))]
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Sampling window over which failure ratio is measured.</summary>
    [JsonConverter(typeof(TimeSpanSecondsConverter))]
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
}
