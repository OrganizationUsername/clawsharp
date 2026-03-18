using Clawsharp.Config.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Clawsharp.Core.Resilience;

/// <summary>
///     Registers per-channel Polly resilience pipelines in <see cref="ResiliencePipelineProvider{TKey}" />.
///     Channels inject the provider and retrieve their pipeline by channel name key.
/// </summary>
public static class ChannelResilienceExtensions
{
    /// <summary>
    ///     Registers a resilience retry pipeline for each configured channel.
    ///     Pipeline key is the channel name (e.g. "email", "mattermost").
    /// </summary>
    public static IServiceCollection AddChannelResiliencePipelines(
        this IServiceCollection services,
        IReadOnlyDictionary<string, ChannelConfig> channels)
    {
        foreach (var (name, cfg) in channels)
        {
            var minBackoff = cfg.MinBackoff;
            var maxBackoff = cfg.MaxBackoff;
            var maxRetries = cfg.MaxRetryAttempts;

            services.AddResiliencePipeline(name, (builder, context) =>
            {
                var logger = context.ServiceProvider
                                    .GetRequiredService<ILoggerFactory>()
                                    .CreateLogger($"Clawsharp.Channels.{name}");

                builder.AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    Delay = minBackoff,
                    MaxDelay = maxBackoff,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<Exception>(ex => ex is not OperationCanceledException),
                    OnRetry = args =>
                    {
                        if (args.Outcome.Exception is not null)
                        {
                            logger.LogError(args.Outcome.Exception,
                                "{Channel} connection error, retrying with backoff", name);
                        }

                        return default;
                    }
                });
            });
        }

        return services;
    }
}
