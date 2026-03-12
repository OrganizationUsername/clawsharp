using Clawsharp.Core.Pipeline;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Features.Chat.Queries;

/// <summary>
/// Determines which LLM model to use for a given message by applying intelligent
/// model routing. When routing is enabled, simple messages (scoring below the
/// configured threshold) are dispatched to a cheaper/faster model; complex messages
/// use the primary model. Per-message model overrides (e.g. from cron jobs) bypass
/// routing entirely. Encapsulates the routing logic previously in
/// <c>AgentLoop.DispatchToProviderAsync</c> (lines 524-544).
/// </summary>
[Handler]
public static partial class RouteModel
{
    /// <summary>Logger category for DI resolution (static types cannot be used as type arguments).</summary>
    public sealed class Log;

    public sealed record Query(
        /// <summary>The user's message text for complexity scoring.</summary>
        string UserText,
        /// <summary>Number of tool-role messages in the last few session turns.</summary>
        int RecentToolCalls,
        /// <summary>Total number of messages in the session (conversation depth).</summary>
        int ConversationDepth,
        /// <summary>Explicit model override (e.g. from cron job config). Null means use routing.</summary>
        string? ModelOverride);

    public sealed record Result(
        /// <summary>The selected model identifier.</summary>
        string Model,
        /// <summary>True if intelligent routing selected the simple model.</summary>
        bool WasRouted);

    private static ValueTask<Result> HandleAsync(
        Query query,
        IOptions<AgentDefaults> defaults,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        var cfg = defaults.Value;

        // Per-message model override takes highest priority — skip routing entirely.
        if (query.ModelOverride is not null)
        {
            return ValueTask.FromResult(new Result(query.ModelOverride, WasRouted: false));
        }

        // Intelligent model routing: score complexity and dispatch to simple model if below threshold.
        if (cfg.ModelRouting is { Enabled: true, SimpleModel: not null } routing)
        {
            var score = ComplexityScorer.Score(query.UserText, query.RecentToolCalls, query.ConversationDepth);

            if (score < routing.Threshold)
            {
                logger.LogDebug(
                    "Model routing: score {Score} < threshold {Threshold}, using simple model {SimpleModel}",
                    score, routing.Threshold, routing.SimpleModel);
                return ValueTask.FromResult(new Result(routing.SimpleModel, WasRouted: true));
            }

            logger.LogDebug(
                "Model routing: score {Score} >= threshold {Threshold}, using primary model {PrimaryModel}",
                score, routing.Threshold, cfg.Model);
        }

        return ValueTask.FromResult(new Result(cfg.Model, WasRouted: false));
    }
}