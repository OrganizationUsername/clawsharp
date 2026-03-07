using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;

namespace Clawsharp.Config.Agent;

/// <summary>Default settings for the agent loop.</summary>
public sealed class AgentDefaults
{
    /// <summary>Default LLM provider name.</summary>
    [Required]
    public string Provider { get; set; } = "openai";

    /// <summary>Default model identifier.</summary>
    [Required]
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Default sampling temperature.</summary>
    [Range(0f, 2f)]
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Maximum number of tool call iterations per request.</summary>
    [Range(1, int.MaxValue)]
    public int MaxToolIterations { get; set; } = 40;

    /// <summary>Maximum number of context messages retained in a session.</summary>
    [Range(1, int.MaxValue)]
    public int MaxContextMessages { get; set; } = 100;

    /// <summary>Number of messages between automatic memory consolidation.</summary>
    [Range(1, int.MaxValue)]
    public int ConsolidateEvery { get; set; } = 20;

    /// <summary>Maximum number of requests per rate limit window.</summary>
    [Range(1, int.MaxValue)]
    public int RateLimitRequests { get; set; } = 20;

    /// <summary>Rate limit window duration in seconds.</summary>
    [Range(1, int.MaxValue)]
    public int RateLimitWindowSeconds { get; set; } = 60;

    /// <summary>Session pruning settings. Null or missing fields disable that pruning axis.</summary>
    [ValidateObjectMembers]
    public SessionPruningConfig? SessionPruning { get; set; }

    /// <summary>Heartbeat service configuration. Null or missing disables the heartbeat.</summary>
    public HeartbeatConfig? Heartbeat { get; set; }

    /// <summary>Allow the spawn_agent tool to create sub-agent loops. Off by default for safety.</summary>
    public bool AllowSubagents { get; set; }

    /// <summary>
    /// Wrap untrusted content (user messages from non-CLI channels, tool results) in XML delimiters
    /// and scan for adversarial directive phrases. Enabled by default.
    /// </summary>
    public bool PromptInjectionGuard { get; set; } = true;

    /// <summary>Enable automatic fallback to other providers on retriable errors.</summary>
    public bool EnableProviderFallback { get; set; }

    /// <summary>
    ///     Ordered list of fallback entries to try when the primary provider fails.
    ///     Each entry can be a plain string (provider name) or an object with
    ///     <c>provider</c>, optional <c>apiKey</c>, <c>model</c>, <c>baseUrl</c>, and <c>authHeader</c>.
    ///     The <c>provider</c> field must be a key in the <c>providers</c> configuration section.
    /// </summary>
    public List<FallbackModelEntry>? FallbackModels { get; set; }

    /// <summary>Context window guard configuration.</summary>
    public ContextWindowConfig? ContextWindow { get; set; }

    /// <summary>Compaction configuration for managing long conversations.</summary>
    public CompactionConfig? Compaction { get; set; }

    /// <summary>
    /// Prompt caching configuration. Null or absent uses all defaults (caching fully enabled).
    /// Set <see cref="CachingConfig.Enabled"/> to false to suppress all cache annotations.
    /// </summary>
    public CachingConfig? Caching { get; set; }

    /// <summary>
    /// Extended thinking / reasoning configuration. Null or absent disables extended thinking.
    /// Each provider uses a different mechanism controlled by separate fields.
    /// </summary>
    public ThinkingConfig? Thinking { get; set; }

    /// <summary>
    /// Intelligent model routing configuration. When enabled, simple messages are routed to a
    /// cheaper/faster model based on a complexity score. Null or absent disables routing.
    /// </summary>
    public ModelRoutingConfig? ModelRouting { get; set; }

    /// <summary>
    /// Provider health check configuration. When enabled, the gateway periodically checks
    /// provider connectivity and logs warnings for unreachable providers. Null or absent disables health checks.
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; set; }
}