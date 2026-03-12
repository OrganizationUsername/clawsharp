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
    public AgentConfig Agents { get; set; } = new();

    /// <summary>LLM provider configurations keyed by provider name.</summary>
    public Dictionary<string, ProviderConfig> Providers { get; set; } = [];

    /// <summary>Channel configurations keyed by channel name.</summary>
    public Dictionary<string, ChannelConfig> Channels { get; set; } = [];

    /// <summary>Memory backend configuration.</summary>
    [ValidateObjectMembers]
    public MemoryConfig Memory { get; set; } = new();

    /// <summary>Tool configuration (workspace, Brave search, shell approval).</summary>
    public ToolsConfig Tools { get; set; } = new();

    /// <summary>Cron job entries seeded from configuration.</summary>
    public List<CronEntry> Cron { get; set; } = [];

    /// <summary>Cost tracking and budget enforcement configuration.</summary>
    public CostConfig? Cost { get; set; }

    /// <summary>Audit logging configuration.</summary>
    public AuditConfig? Audit { get; set; }

    /// <summary>MCP server configurations keyed by server name.</summary>
    public Dictionary<string, McpServerConfig>? McpServers { get; set; }

    /// <summary>Security configuration.</summary>
    public SecurityConfig? Security { get; set; }

    /// <summary>At-rest secrets encryption configuration.</summary>
    public SecretsConfig? Secrets { get; set; }

    /// <summary>Voice transcription configuration (Groq Whisper / OpenAI Whisper), shared by all channels.</summary>
    public TranscriptionConfig? Transcription { get; set; }

    /// <summary>HTTP request settings (proxy) for outbound LLM provider calls.</summary>
    public HttpRequestConfig? HttpRequest { get; set; }

    /// <summary>Interaction analytics configuration (prompt/response/cost logging).</summary>
    public AnalyticsConfig? Analytics { get; set; }
}