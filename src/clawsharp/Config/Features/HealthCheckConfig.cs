namespace Clawsharp.Config.Features;

/// <summary>
///     Configuration for periodic provider health checks. When enabled, the gateway
///     verifies provider connectivity on startup and at regular intervals.
/// </summary>
public sealed class HealthCheckConfig
{
    /// <summary>Whether provider health checks are enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Interval in seconds between periodic health checks.</summary>
    public int IntervalSeconds { get; init; } = 300;

    /// <summary>Whether to check provider health immediately on startup.</summary>
    public bool CheckOnStartup { get; init; } = true;
}
