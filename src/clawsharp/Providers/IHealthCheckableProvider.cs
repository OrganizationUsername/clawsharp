namespace Clawsharp.Providers;

/// <summary>
///     Optional interface for providers that support connectivity health checks.
///     Not all providers expose a suitable endpoint; only implement this on providers
///     where a lightweight ping or list-models call is available.
/// </summary>
public interface IHealthCheckableProvider
{
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default);
}
