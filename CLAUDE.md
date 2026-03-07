# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`clawsharp` is a .NET 10 console application — a self-hosted, channel-agnostic AI assistant gateway. It connects to 18 messaging platforms via pluggable LLM providers (33 providers) with 22 tools and 4 memory backends. Licensed Apache-2.0.

## Repo Layout

```
src/clawsharp/          Main .NET project
src/clawsharp-web/      Svelte web UI (embedded into binary via MSBuild target)
tests/clawsharp.Tests/  NUnit tests (1024+ non-integration)
benchmarks/             BenchmarkDotNet projects
clawsharp.slnx          Solution file (.slnx format, not .sln)
```

## Commands

```bash
# Build (JIT, fast iteration)
dotnet build src/clawsharp/clawsharp.csproj

# Run
dotnet run --project src/clawsharp/clawsharp.csproj

# Run all non-Docker tests (fast)
dotnet test tests/clawsharp.Tests/clawsharp.Tests.csproj --filter "FullyQualifiedName!~Integration"

# Run a single test class or method
dotnet test tests/clawsharp.Tests/clawsharp.Tests.csproj --filter "FullyQualifiedName~CostTrackerTests"

# Run integration tests (requires Docker / Testcontainers)
dotnet test tests/clawsharp.Tests/clawsharp.Tests.csproj --filter "FullyQualifiedName~Integration"

# Publish self-contained single-file binary
dotnet publish src/clawsharp/clawsharp.csproj -c Release -r linux-x64    # → dist/linux-x64/clawsharp
dotnet publish src/clawsharp/clawsharp.csproj -c Release -r win-x64     # → dist/win-x64/clawsharp.exe

# EF Core migrations
dotnet ef migrations add MyMigration --context SqliteMemoryContext --project src/clawsharp/clawsharp.csproj --output-dir Memory/Sqlite/Migrations
dotnet ef migrations add MyMigration --context PostgresMemoryContext --project src/clawsharp/clawsharp.csproj --output-dir Memory/Postgres/Migrations
dotnet ef migrations add MyMigration --context MsSqlMemoryContext --project src/clawsharp/clawsharp.csproj --output-dir Memory/MsSql/Migrations

# Docker
docker compose up --build
docker compose --profile postgres up
docker compose --profile mssql up
```

The solution file uses the newer `.slnx` XML format (`clawsharp.slnx`), not `.sln`.

## Architecture

### Runtime Constraints

- **Target framework**: .NET 10 with `LangVersion=preview`
- **Nullable reference types**: enabled — all code must be null-safe
- **Globalization**: `InvariantGlobalization=true` — prefer `ToString("O")` for dates, `StringComparison.Ordinal` for comparisons
- **NativeAOT**: `PublishAot` was removed (blocked by Intellenum SharedTypes assembly at runtime + EF Core incompatibility); `EnableConfigurationBindingGenerator=true` is kept for trim analysis

### Request Flow

```
IChannel.ReceiveAsync() → InboundMessage → AgentLoop.ProcessMessageAsync()
  → SessionManager (load ~/.clawsharp/sessions/{channel}:{senderId}.json)
  → SlashCommandRouter (/help, /usage, /clear, /forget, /reset, ...)
  → RateLimiter → CostTracker.CheckBudgetAsync()
  → SystemPromptBuilder.BuildSplit() → (StaticPart, DynamicPart)
  → IProvider.ChatAsync(ChatRequest) or IStreamingProvider.StreamAsync()
      [tool calls → IToolRegistry.ExecuteAsync(), up to MaxToolIterations]
  → CostTracker.RecordUsageAsync()
  → SessionManager.SaveAsync() (atomic File.Move)
  → CompactionService (if messages ≥ ConsolidateEvery)
  → IChannel.SendAsync(OutboundMessage)
```

### VSA/CQRS Handlers (Immediate.Handlers)

Business logic uses vertical slice architecture with `Immediate.Handlers` (source-generated mediator, zero reflection). Handlers live in `Features/` organized by domain:

```
Features/
  Chat/        (4 handlers — SendMessage, BuildChatRequest, ProcessToolCalls, CompactSession)
  Session/     (5 handlers — Load, Save, Clear, List, Prune)
  Cost/        (3 handlers — CheckBudget, RecordUsage, GetSummary)
  Memory/      (5 handlers — Store, Search, Recall, ExtractFacts, Decay)
  Tools/       (1 handler  — ExecuteTool)
  Behaviors/   (pipeline behaviors — validation, logging)
```

Generated registration methods: `AddclawsharpHandlers()` / `AddclawsharpBehaviors()` (lowercase 'c' — uses raw assembly name). Handler lifetime is `ServiceLifetime.Singleton`.

### Dependency Injection

All services are registered manually in `GatewayHost.cs`. Channels use the singleton + hosted-service pattern (`AddSingleton<T>()` + `AddHostedService(sp => sp.GetRequiredService<T>())`) so they are discoverable via `GetServices<IHostedService>().OfType<IChannel>()`. Plain singletons use `AddSingleton<T>()`. Non-channel hosted services use `AddHostedService<T>()`.

**Critical DI rule**: NEVER resolve `GetServices<IHostedService>()` inside a singleton factory consumed by a hosted service constructor — this causes circular dependency. Channels use triple-registration via `AddChannel<T>()` helper.

### JSON Serialization

All JSON uses **source-generated contexts** — no reflection. Each subsystem has its own `JsonSerializerContext`:

| Context | Covers |
|---------|--------|
| `ConfigJsonContext` | `AppConfig` and all sub-configs |
| `SessionJsonContext` | `Session`, `ChatMessage`, `ToolCall` |
| `AnthropicJsonContext` | Anthropic API DTOs |
| `OpenAiJsonContext` | OpenAI-compatible API DTOs |
| `GeminiJsonContext` | Gemini API DTOs |
| `BedrockJsonContext` | AWS Bedrock DTOs |
| `AuditJsonContext` | `AuditEvent` |
| Channel-specific contexts | `MatrixJsonContext`, `SlackJsonContext`, `TelegramJsonContext`, `WebJsonContext` |

**When adding a new config class**, register it in `Config/JsonContext.cs` with `[JsonSerializable(typeof(MyNewConfig))]`.

### String-Backed Enums (Intellenum)

Intellenum generates value-objects used as type-safe string constants:
- `ChannelName` — "cli", "telegram", "discord", "slack", "matrix", "email", "irc", "web"
- `LlmProviderType` — "openai", "anthropic", "gemini", "bedrock", "ollama", etc.
- `MemoryBackend` — "markdown", "sqlite", "postgres", "mssql"
- `MessageRole`, `FinishReason`, `CronScheduleKind`, `CronSource`

Use `ChannelName.TryFromValue("telegram", out var cn)` and `.Value` to get the string. Never use plain strings in place of these types.

### Providers

`IProvider` requires `ChatAsync(ChatRequest) → ChatResponse`. `IStreamingProvider` extends it with `StreamAsync() → IAsyncEnumerable<StreamChunk>`. Concrete implementations live in `Providers/{Name}/`.

Most cloud providers route through `OpenAiProvider` with a different `baseUrl` (OpenRouter, Groq, DeepSeek, Mistral, Perplexity, xAI, Fireworks, Cerebras, etc.). The exceptions with dedicated implementations are: **Anthropic**, **Gemini**, **AWS Bedrock**, **GitHub Copilot**, and local servers (Ollama, LM Studio).

**Prompt caching**: `ChatRequest` carries `SystemStaticPart`/`SystemDynamicPart` (from `SystemPromptBuilder.BuildSplit()`) and `CacheToolDefinitions`. `AnthropicProvider` adds `cache_control: ephemeral` to the static system block and optionally the last tool definition. `OpenAiProvider` reads back `prompt_tokens_details.cached_tokens`. Both propagate `CacheReadTokens`/`CacheWriteTokens` into `ChatResponse`. The toggle is `agents.defaults.caching: { enabled, cacheToolDefinitions }` in config.

### Tools

Tools extend the abstract `Tool` base class:
- `Name` — snake_case identifier shown to the LLM
- `Description` — tool description
- `ParametersSchemaJson` — inline JSON Schema string
- `ExecuteAsync(JsonElement arguments, CancellationToken)` — returns a string result

`IToolRegistry` manages registration and dispatch. Always use `PathGuard.SafeResolve()` for filesystem paths, `SsrfGuard.CheckAsync()` before any HTTP fetch, and `AuditLogger.LogAsync()` for sensitive operations.

### Memory Backends

`IMemory` is the unified interface. Four implementations selected via `memory.backend` in config:

| Backend | Notes |
|---------|-------|
| `markdown` | Flat file `~/.clawsharp/memory.md` |
| `sqlite` | EF Core, local file; hybrid FTS5 + cosine search |
| `postgres` | EF Core + Npgsql; hybrid tsquery + cosine search |
| `mssql` | EF Core + SqlClient |

Hybrid search: FTS5/tsquery pre-filter capped at 500 candidates → in-process cosine scoring via `EmbeddingMath`. Memory decay scoring uses `AccessCount`/`LastAccessedAt` for usage-weighted pruning.

### Sessions and Compaction

Session files live at `~/.clawsharp/sessions/{channel}:{senderId}.json` and are written atomically via `File.Move`. The `Session` record holds `Messages: List<ChatMessage>` (capped at `MaxContextMessages`), `TotalInputTokens`, `TotalOutputTokens`.

`CompactionService` summarizes old messages via an LLM call when `messages.Count % ConsolidateEvery == 0`, replacing the older half of the history with a summary message.

### Security Subsystem

| Component | Purpose |
|-----------|---------|
| `PathGuard` | Resolves paths within workspace; rejects traversal |
| `SsrfGuard` | Blocks private IPs, link-local ranges, cloud metadata endpoints |
| `ShellGuard` | Blocks dangerous shell patterns |
| `PromptGuard` | XML-wraps untrusted content; scans for injection directive phrases |
| `LeakDetector` | Regex-scans output for secrets/PII before delivery |
| `AuditLogger` | Appends JSONL audit events to `~/.clawsharp/audit.jsonl` |
| `SecretStore` | AES-GCM encryption for secrets at rest in config.json |
| `PasswordManagerResolver` | Resolves `op://vault/item/field` (1Password) and `bws:<uuid>` (Bitwarden) refs |
| `WebPairingGuard` | TOTP-style 6-digit codes for web channel pairing |

### Cost Tracking

`CostTracker` (singleton) persists records to `~/.clawsharp/costs.jsonl` via `CostStorage`. Budget is checked before each LLM call and usage is recorded after. `DefaultPricing` maps model name prefixes to per-token USD rates; unknown models record zero cost. `CostSummary` exposes `Daily`, `Monthly`, `Session`, plus the corresponding `*Savings` fields for cache-read discounts.

### Config Structure

`AppConfig` (root) → `Agents.Defaults: AgentDefaults`:
- Core: `Provider`, `Model`, `Temperature`, `MaxToolIterations`, `MaxContextMessages`
- Session: `ConsolidateEvery`, `RateLimitRequests`, `RateLimitWindowSeconds`
- Optional: `Caching`, `Compaction`, `ContextWindow`, `SessionPruning`, `Heartbeat`, `FallbackModels`

Top-level optional sections: `Cost`, `Audit`, `McpServers`, `Security`, `Secrets`, `Transcription`.

Config is loaded from `~/.clawsharp/config.json`. Use `clawsharp config set key=value` to modify — always register new config types in `Config/JsonContext.cs`.

### Channels

18 channels: **CLI**, **Telegram**, **Discord** (Remora.Discord), **Slack** (Socket Mode), **Matrix**, **Email** (MailKit IMAP/SMTP), **IRC**, **Web** (embedded HTML + SSE), **WhatsApp**, **Signal**, **iMessage** (BlueBubbles), **Nostr**, **Mattermost**, **Line**, **Lark**, **WeChat**, **WeCom**, **QQ**. Each implements `IChannel` and is registered as a hosted service. All HTTP calls use named `IHttpClientFactory` clients with Polly resilience — never `new HttpClient()`.

### Voice Transcription

`VoiceTranscriptionService` (singleton) handles audio on Telegram, WhatsApp, Signal, Discord. Backends: Groq/OpenAI Whisper (multipart/form-data), Azure Fast Transcription (`speech.microsoft.com`), GCP Speech-to-Text v1 (base64 JSON body). Speaker diarization available on Azure and GCP.

## Key Conventions

- **Intellenum enums**: Use `TryFromValue()` (not `TryFromName()`) and `.Value` for the string. Never use raw strings where `ChannelName`, `LlmProviderType`, or `MemoryBackend` exist.
- **AllowFrom semantics**: `null` = allow all, `[]` = deny all, `["*"]` = wildcard allow.
- **Embedded resource name**: `clawsharp.Channels.Web.index.html` (lowercase assembly name prefix).
- **AgentLoop is partial**: Split across 5 files in `Core/Pipeline/` — `AgentLoop.cs`, `.Streaming.cs`, `.ToolExecution.cs`, `.SlashCommands.cs`, `.Pipeline.cs`. The `AgentHandlers` record groups 13 handler dependencies into a single DI-injectable type.
- **New config classes**: Register in `Config/JsonContext.cs` with `[JsonSerializable(typeof(MyNewConfig))]`.
- **New channels**: Register via `AddChannel<T>()` helper in `GatewayHost.cs`.
- **DTO properties**: Use `{ get; init; }` by default. Only use `{ get; set; }` for properties mutated after construction (EF Core tracked entities, status fields).

## Agents & Tools for clawsharp

See the parent `CLAUDE.md` (monorepo root) for the full agent catalog and MCP server reference. clawsharp-specific guidance:

### Preferred Agents by Task

| Task | Agent |
|------|-------|
| C# / .NET implementation | `csharp-developer` or `dotnet-core-expert` |
| SQL schema / query work | `sql-pro` |
| Docker / deployment | `docker-expert` or `devops-engineer` |
| Security review (channels, tools, auth) | `security-engineer` |
| Code review after changes | `code-reviewer` |
| Debugging build or runtime errors | `gsd-debugger` |
| Phase planning | `gsd-planner` → `gsd-executor` |
| .NET API questions | `microsoft-learn` MCP (docs + code samples) |

### MCP Tool Priorities

- **Build validation**: use `mcp__Rider__build_project` after edits — richer error output than `dotnet build`
- **Code search**: use `mcp__Rider__search_text` / `search_symbol` — IDE-indexed, faster than Grep for large searches
- **Semantic rename**: use `mcp__Rider__rename_refactoring` — updates all references project-wide
- **.NET docs**: use `mcp__microsoft-learn__microsoft_docs_search` before WebSearch for any C#/.NET question
- **Reasoning**: use `mcp__sequentialthinking__sequentialthinking` before designing new subsystems or agent-loop changes
