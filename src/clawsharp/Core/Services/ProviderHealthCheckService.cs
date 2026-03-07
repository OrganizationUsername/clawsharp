using Clawsharp.Config;
using Clawsharp.Config.Features;
using Clawsharp.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Core.Services;

/// <summary>
///     Background service that periodically checks provider connectivity via
///     <see cref="IHealthCheckableProvider.CheckHealthAsync"/>. Logs healthy status at
///     Information level, unhealthy at Warning (periodic) or Error (startup).
/// </summary>
public sealed partial class ProviderHealthCheckService : LifecycleBackgroundService
{
    private readonly IProvider _primaryProvider;

    private readonly AppConfig _appConfig;

    private readonly HealthCheckConfig _healthCheckConfig;

    private readonly IHttpClientFactory _httpFactory;

    private readonly ILogger<ProviderHealthCheckService> _logger;

    public ProviderHealthCheckService(
        IProvider primaryProvider,
        IOptions<AppConfig> configOptions,
        IHttpClientFactory httpFactory,
        ILogger<ProviderHealthCheckService> logger)
    {
        _primaryProvider = primaryProvider;
        _appConfig = configOptions.Value;
        _healthCheckConfig = _appConfig.Agents.Defaults.HealthCheck
                             ?? throw new InvalidOperationException(
                                 "ProviderHealthCheckService registered but HealthCheck config is null.");
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_healthCheckConfig.CheckOnStartup)
        {
            await CheckAllProvidersAsync(isStartup: true, stoppingToken).ConfigureAwait(false);
        }

        var interval = TimeSpan.FromSeconds(_healthCheckConfig.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                await CheckAllProvidersAsync(isStartup: false, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogHealthCheckLoopError(_logger, ex);
            }
        }
    }

    private async Task CheckAllProvidersAsync(bool isStartup, CancellationToken ct)
    {
        // Check primary provider
        await CheckProviderAsync(_primaryProvider, "primary", isStartup, ct).ConfigureAwait(false);

        // Check fallback providers if fallback is enabled
        if (_appConfig.Agents.Defaults is { EnableProviderFallback: true, FallbackModels: { Count: > 0 } fallbacks })
        {
            foreach (var fallback in fallbacks)
            {
                try
                {
                    var provider = ProviderFactory.Create(
                        fallback.Provider,
                        _appConfig.Providers,
                        _httpFactory,
                        deviceFlow: null,
                        apiKeyOverride: fallback.ApiKey,
                        baseUrlOverride: fallback.BaseUrl,
                        authHeaderOverride: fallback.AuthHeader);

                    await CheckProviderAsync(provider, $"fallback:{fallback.Provider}", isStartup, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogFallbackProviderCreateError(_logger, fallback.Provider, ex);
                }
            }
        }
    }

    private async Task CheckProviderAsync(IProvider provider, string role, bool isStartup, CancellationToken ct)
    {
        if (provider is not IHealthCheckableProvider healthCheckable)
        {
            LogProviderNotCheckable(_logger, provider.Name, role);
            return;
        }

        try
        {
            var result = await healthCheckable.CheckHealthAsync(ct).ConfigureAwait(false);

            if (result.IsHealthy)
            {
                LogProviderHealthy(_logger, provider.Name, role,
                    result.ResponseTime?.TotalMilliseconds ?? 0, result.Message ?? "OK");
            }
            else if (isStartup)
            {
                LogProviderUnhealthyStartup(_logger, provider.Name, role, result.Message ?? "Unknown error");
            }
            else
            {
                LogProviderUnhealthyPeriodic(_logger, provider.Name, role, result.Message ?? "Unknown error");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — don't log
        }
        catch (Exception ex)
        {
            if (isStartup)
            {
                LogProviderCheckFailedStartup(_logger, provider.Name, role, ex);
            }
            else
            {
                LogProviderCheckFailedPeriodic(_logger, provider.Name, role, ex);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Provider '{ProviderName}' ({Role}) is healthy [{ResponseTimeMs:F0}ms] — {Message}")]
    private static partial void LogProviderHealthy(ILogger logger, string providerName, string role,
        double responseTimeMs, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "Provider '{ProviderName}' ({Role}) is UNHEALTHY on startup — {Message}")]
    private static partial void LogProviderUnhealthyStartup(ILogger logger, string providerName, string role,
        string message);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Provider '{ProviderName}' ({Role}) is UNHEALTHY — {Message}")]
    private static partial void LogProviderUnhealthyPeriodic(ILogger logger, string providerName, string role,
        string message);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Health check for provider '{ProviderName}' ({Role}) threw an exception on startup")]
    private static partial void LogProviderCheckFailedStartup(ILogger logger, string providerName, string role,
        Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Health check for provider '{ProviderName}' ({Role}) threw an exception")]
    private static partial void LogProviderCheckFailedPeriodic(ILogger logger, string providerName, string role,
        Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
        Message = "Provider '{ProviderName}' ({Role}) does not support health checks, skipping")]
    private static partial void LogProviderNotCheckable(ILogger logger, string providerName, string role);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error,
        Message = "Health check loop encountered an error")]
    private static partial void LogHealthCheckLoopError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Could not create fallback provider '{ProviderName}' for health check")]
    private static partial void LogFallbackProviderCreateError(ILogger logger, string providerName,
        Exception exception);
}
