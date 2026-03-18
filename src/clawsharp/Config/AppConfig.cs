using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Channels;
using Clawsharp.Config.Features;
using Clawsharp.Config.Memory;
using Clawsharp.Config.Security;

namespace Clawsharp.Config;

/// <summary>Root application configuration.</summary>
public sealed class AppConfig
{
    /// <summary>Agent configuration (defaults, model, provider).</summary>
    [ValidateObjectMembers]
    public AgentConfig Agents { get; init; } = new();

    /// <summary>LLM provider configurations keyed by provider name.</summary>
    public Dictionary<string, ProviderConfig> Providers { get; init; } = [];

    /// <summary>Channel configurations keyed by channel name.</summary>
    public Dictionary<string, ChannelConfig> Channels { get; init; } = [];

    /// <summary>Memory backend configuration.</summary>
    [ValidateObjectMembers]
    public MemoryConfig Memory { get; init; } = new();

    /// <summary>Tool configuration (workspace, Brave search, shell approval).</summary>
    public ToolsConfig Tools { get; init; } = new();

    /// <summary>Cron job entries seeded from configuration.</summary>
    public List<CronEntry> Cron { get; init; } = [];

    /// <summary>Cost tracking and budget enforcement configuration.</summary>
    public CostConfig? Cost { get; init; }

    /// <summary>Audit logging configuration.</summary>
    public AuditConfig? Audit { get; init; }

    /// <summary>MCP server configurations keyed by server name.</summary>
    public Dictionary<string, McpServerConfig>? McpServers { get; init; }

    /// <summary>Security configuration.</summary>
    public SecurityConfig? Security { get; init; }

    /// <summary>At-rest secrets encryption configuration.</summary>
    public SecretsConfig? Secrets { get; init; }

    /// <summary>Voice transcription configuration (Groq Whisper / OpenAI Whisper), shared by all channels.</summary>
    public TranscriptionConfig? Transcription { get; init; }

    /// <summary>HTTP request settings (proxy) for outbound LLM provider calls.</summary>
    public HttpRequestConfig? HttpRequest { get; init; }

    /// <summary>Interaction analytics configuration (prompt/response/cost logging).</summary>
    public AnalyticsConfig? Analytics { get; init; }

    /// <summary>Configurable size limits for media attachments (images, voice files).</summary>
    public LimitsConfig Limits { get; init; } = new();
}