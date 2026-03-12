using System.Text.Json.Serialization;
using Clawsharp.Memory.Entities;
using Clawsharp.Security;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Channels;
using Clawsharp.Config.Features;
using Clawsharp.Config.Memory;
using Clawsharp.Config.Search;
using Clawsharp.Config.Security;

namespace Clawsharp.Config;

[JsonSerializable(typeof(AppConfig)), JsonSerializable(typeof(AgentConfig)), JsonSerializable(typeof(AgentDefaults)),
 JsonSerializable(typeof(CachingConfig)),
 JsonSerializable(typeof(ProviderConfig)), JsonSerializable(typeof(ChannelConfig)), JsonSerializable(typeof(MemoryConfig)),
 JsonSerializable(typeof(ToolsConfig)), JsonSerializable(typeof(BraveConfig)), JsonSerializable(typeof(CronEntry)),
 JsonSerializable(typeof(SessionPruningConfig)), JsonSerializable(typeof(HeartbeatConfig)),
 JsonSerializable(typeof(McpServerConfig)),
 // Cost tracking
 JsonSerializable(typeof(CostConfig)), JsonSerializable(typeof(ModelPricing)),
 JsonSerializable(typeof(Dictionary<string, ModelPricing>)),
 // Audit
 JsonSerializable(typeof(AuditConfig)),
 // Security children
 JsonSerializable(typeof(SecurityConfig)), JsonSerializable(typeof(SsrfConfig)),
 JsonSerializable(typeof(PromptGuardConfig)), JsonSerializable(typeof(LeakDetectorConfig)),
 JsonSerializable(typeof(CanaryGuardConfig)),
 // Secrets children
 JsonSerializable(typeof(SecretsConfig)), JsonSerializable(typeof(OnePasswordConfig)),
 JsonSerializable(typeof(BitwardenConfig)),
 // Transcription
 JsonSerializable(typeof(TranscriptionConfig)),
 // AgentDefaults children
 JsonSerializable(typeof(CompactionConfig)), JsonSerializable(typeof(ContextWindowConfig)),
 JsonSerializable(typeof(ThinkingConfig)), JsonSerializable(typeof(ModelRoutingConfig)),
 JsonSerializable(typeof(FallbackModelEntry)), JsonSerializable(typeof(List<FallbackModelEntry>)),
 // Memory children
 JsonSerializable(typeof(EmbeddingConfig)), JsonSerializable(typeof(MemoryDecayConfig)),
 JsonSerializable(typeof(EnhancedRecallConfig)), JsonSerializable(typeof(FactExtractionConfig)),
 // Provider health checks
 JsonSerializable(typeof(HealthCheckConfig)),
 // Browser automation
 JsonSerializable(typeof(BrowserConfig)),
 JsonSerializable(typeof(PinchTabConfig)),
 // Tool filter groups
 JsonSerializable(typeof(ToolFilterGroup)),
 JsonSerializable(typeof(Dictionary<string, ToolFilterGroup>)),
 // Search API configs
 JsonSerializable(typeof(ExaConfig)), JsonSerializable(typeof(TavilyConfig)),
 JsonSerializable(typeof(JinaConfig)), JsonSerializable(typeof(FirecrawlConfig)),
 JsonSerializable(typeof(SearxngConfig)), JsonSerializable(typeof(PerplexityConfig)),
 JsonSerializable(typeof(GlmConfig)),
 // Security
 JsonSerializable(typeof(LandlockConfig)),
 // HTTP request
 JsonSerializable(typeof(HttpRequestConfig)),
 // Analytics
 JsonSerializable(typeof(AnalyticsConfig)),
 // Collections
 JsonSerializable(typeof(List<CronEntry>)), JsonSerializable(typeof(Dictionary<string, ProviderConfig>)),
 JsonSerializable(typeof(Dictionary<string, McpServerConfig>)),
 JsonSerializable(typeof(Dictionary<string, ChannelConfig>)),
 JsonSerializable(typeof(Fact)), JsonSerializable(typeof(List<Fact>)), JsonSourceGenerationOptions(
     PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
     WriteIndented = true,
     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ConfigJsonContext : JsonSerializerContext;