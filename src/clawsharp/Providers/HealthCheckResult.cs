namespace Clawsharp.Providers;

/// <summary>
///     Result of a provider health check. Contains connectivity status, an optional diagnostic
///     message, and the time taken for the check.
/// </summary>
public sealed record HealthCheckResult(bool IsHealthy, string? Message = null, TimeSpan? ResponseTime = null);
