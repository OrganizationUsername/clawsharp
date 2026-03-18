# clawsharp

A self-hosted, channel-agnostic AI assistant gateway built on .NET 10. Connect any LLM provider to 18 messaging platforms through a single binary or container — with 22 built-in tools, 4 memory backends, and defense-in-depth security.

## What is clawsharp?

clawsharp is a personal AI assistant that runs on your own hardware. You bring your own API keys, choose your LLM provider, and connect it to the messaging platforms you already use. Everything stays local — your conversations, memory, and credentials never leave your machine.

The assistant can read and write files, browse the web, run shell commands, manage goals, search your memory, and extend itself through MCP (Model Context Protocol) servers. It remembers context across conversations with hybrid full-text + vector search, and can route simple queries to cheaper models automatically.

### Key capabilities

- **18 messaging channels** — Telegram, Discord, Slack, Matrix, IRC, email, web UI, WhatsApp, Signal, iMessage, Nostr, Mattermost, Line, Lark, WeChat, WeCom, QQ, and a local CLI
- **33 LLM providers** — OpenAI, Anthropic, Gemini, AWS Bedrock, Ollama, LM Studio, GitHub Copilot, and 26 more via OpenAI-compatible routing (Groq, DeepSeek, Mistral, xAI, Fireworks, Cerebras, Together, Cohere, and others)
- **22 built-in tools** — file operations, shell, git, web fetch/search, browser automation, memory, goals, cron scheduling, document parsing, and file sending
- **9 web search backends** — Brave, SearXNG, Exa, Tavily, Jina, Firecrawl, Perplexity, GLM, plus any MCP search tool
- **4 memory backends** — Markdown files, SQLite, PostgreSQL, SQL Server — all with hybrid FTS + cosine vector search
- **MCP server hosting** — stdio, SSE, and StreamableHTTP transports for extending the tool set

---

## Getting Started

### Docker (recommended)

Running in a container is the safest way to operate an AI agent. The container isolates the assistant from your host filesystem and limits tool access to the mounted workspace only.

```bash
git clone https://github.com/ClawSharp/clawsharp.git
cd clawsharp

# Generate an encryption key for secrets at rest
echo "CLAWSHARP_SECRET_KEY=$(openssl rand -hex 32)" >> .env

# Start
docker compose up --build
```

With a database backend:

```bash
# PostgreSQL
docker compose --profile postgres up

# SQL Server
docker compose --profile mssql up
```

> **Podman** works as a drop-in replacement — just use `podman compose` instead of `docker compose`. Podman runs rootless by default, making it safer for running an AI agent on your machine.

### Native install

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/ClawSharp/clawsharp.git
cd clawsharp

# Build
dotnet build src/clawsharp/clawsharp.csproj

# Run the onboarding wizard
dotnet run --project src/clawsharp/clawsharp.csproj -- onboard

# Start the gateway
dotnet run --project src/clawsharp/clawsharp.csproj
```

### Publish a self-contained binary

```bash
dotnet publish src/clawsharp/clawsharp.csproj -c Release -r linux-x64    # -> dist/linux-x64/clawsharp
dotnet publish src/clawsharp/clawsharp.csproj -c Release -r win-x64     # -> dist/win-x64/clawsharp.exe
dotnet publish src/clawsharp/clawsharp.csproj -c Release -r osx-arm64   # -> dist/osx-arm64/clawsharp
```

---

## Onboarding

The interactive wizard configures your provider, channels, and security settings:

```bash
clawsharp onboard                           # interactive
clawsharp onboard -p anthropic -k sk-ant-...  # non-interactive
```

The wizard will:
1. Choose your LLM provider and model
2. Set up messaging channels
3. Encrypt API keys with ChaCha20-Poly1305 at rest
4. Install default skills (including `skill-vetter` for safe skill management)
5. Write `~/.clawsharp/config.json`

---

## Configuration

Config is loaded in priority order (later sources override earlier):

| Source | Location |
|--------|----------|
| Built-in defaults | (compiled in) |
| User config | `~/.clawsharp/config.json` |
| Local config | `./config.json` |
| Env var path | `CLAWSHARP_CONFIG=/path/to/config.json` |
| `.env` file | `./.env` |
| Environment variables | `CLAWSHARP__SECTION__KEY=value` |

### Minimal config.json

```json
{
  "agents": {
    "defaults": {
      "provider": "anthropic",
      "model": "claude-sonnet-4-6",
      "temperature": 0.7
    }
  },
  "providers": {
    "anthropic": { "type": "anthropic", "apiKey": "sk-ant-..." }
  },
  "channels": {
    "cli": { "enabled": true }
  }
}
```

### Environment variable format

Double underscores map to JSON hierarchy:

```bash
CLAWSHARP__PROVIDERS__OPENAI__APIKEY=sk-...
CLAWSHARP__AGENTS__DEFAULTS__MODEL=claude-sonnet-4-6
CLAWSHARP__MEMORY__BACKEND=postgres
CLAWSHARP__MEMORY__CONNECTIONSTRING="Host=localhost;Database=clawsharp;..."
```

### Secret management

API keys in `config.json` are encrypted at rest with ChaCha20-Poly1305 AEAD. You can also reference external secret managers:

```json
{
  "providers": {
    "anthropic": {
      "type": "anthropic",
      "apiKey": "op://vault/anthropic/api-key"
    }
  }
}
```

Supported references:
- `op://vault/item/field` — 1Password (requires `OP_SERVICE_ACCOUNT_TOKEN`)
- `bws:<secret-uuid>` — Bitwarden Secrets Manager (requires `BWS_ACCESS_TOKEN`)
- `enc2:...` — ChaCha20-Poly1305 encrypted (produced by `clawsharp config encrypt-secrets`)

---

## Channels

| Channel | Config key | Required credentials |
|---------|-----------|---------------------|
| CLI | `cli` | none |
| Telegram | `telegram` | `token` |
| Discord | `discord` | `token` |
| Slack | `slack` | `botToken`, `appToken` |
| Matrix | `matrix` | `homeserver`, `accessToken` |
| Email | `email` | `imapHost`, `smtpHost`, `username`, `password` |
| IRC | `irc` | `host`, `nick` |
| Web UI | `web` | none (optional `pairingToken`) |
| WhatsApp | `whatsapp` | `bridgeUrl`, `token` |
| Signal | `signal` | `bridgeUrl`, `phoneNumber` |
| iMessage | `bluebubbles` | `bridgeUrl`, `password` |
| Nostr | `nostr` | `nostrPrivKey` |
| Mattermost | `mattermost` | `mattermostUrl`, `token` |
| Line | `line` | `token`, `secret` |
| Lark | `lark` | `appId`, `appSecret` |
| WeChat | `wechat` | `token`, `webhookKey` |
| WeCom | `wecom` | `token`, `encodingAesKey` |
| QQ | `qq` | `bridgeUrl` |

### Channel features

- **Streaming** — Telegram, Discord, Slack, Web, and Matrix support live-updating responses via edit-message or SSE
- **Group filtering** — `requireMention` (Telegram, Discord) and `groupPolicy: "mention" | "open"` (Discord) control when the bot responds in group chats
- **Allow lists** — `allowFrom` restricts who can message the bot; `allowRooms`/`allowedChannels` restricts which rooms
- **Voice transcription** — Telegram, WhatsApp, Signal, and Discord audio messages are transcribed via Groq/OpenAI Whisper, Azure Fast Transcription, or GCP Speech-to-Text
- **File sending** — Discord, Slack (3-step upload), and other channels support outbound file delivery
- **Forum/topic threads** — Telegram forum topics and Slack threads get isolated session contexts

---

## Providers

| Provider | Type | Auth |
|----------|------|------|
| OpenAI | `openai` | `apiKey` |
| Anthropic | `anthropic` | `apiKey` |
| Google Gemini | `gemini` | `apiKey` |
| AWS Bedrock | `bedrock` | `awsAccessKeyId`, `awsSecretAccessKey`, `awsRegion` |
| GitHub Copilot | `copilot` | OAuth device flow (`clawsharp auth login-copilot`) |
| Ollama | `ollama` | `baseUrl` (default: `http://localhost:11434`) |
| LM Studio | `lmstudio` | `baseUrl` (default: `http://localhost:1234`) |

Plus 26 providers that route through OpenAI-compatible API: OpenRouter, Groq, DeepSeek, Mistral, Perplexity, xAI, Fireworks, Cerebras, Together AI, Cohere, SambaNova, HuggingFace, AI21, Replicate, Vertex AI, Novita, DashScope, Zhipu, Moonshot, Volcengine, Minimax, SiliconFlow, vLLM, llama.cpp, and any custom OpenAI-compatible endpoint.

### Extended thinking

```json
{
  "agents": {
    "defaults": {
      "thinking": {
        "budgetTokens": 8192,
        "reasoningEffort": "medium",
        "geminiBudgetTokens": 4096
      }
    }
  }
}
```

### Model routing

Automatically route simple queries to a cheaper model:

```json
{
  "agents": {
    "defaults": {
      "modelRouting": {
        "enabled": true,
        "simpleModel": "gpt-4o-mini",
        "simpleProvider": "openai",
        "threshold": 30
      }
    }
  }
}
```

### Prompt caching

Anthropic and OpenAI prompt caching is enabled by default. The system prompt is split into a stable prefix (cached) and a dynamic suffix (date/time). Cache token savings are tracked in cost reports.

### Provider health checks

clawsharp can verify that your LLM provider is reachable on startup and continuously monitor connectivity while running. This is especially useful for local providers like Ollama and LM Studio, where the server may not always be running.

```json
{
  "agents": {
    "defaults": {
      "healthCheck": {
        "enabled": true,
        "interval": "00:05:00",
        "checkOnStartup": true
      }
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `enabled` | `true` | Enable or disable health checks |
| `checkOnStartup` | `true` | Verify provider connectivity immediately on launch |
| `interval` | `"00:05:00"` | How often to re-check provider health (TimeSpan or integer seconds) |

**How it works:**

- On startup, clawsharp sends a lightweight request to the provider's models endpoint (e.g., `GET /v1/models` for OpenAI-compatible providers). If the provider is unreachable, an error is logged immediately so you know before sending your first message.
- When fallback providers are configured, each one is checked independently. You'll see which providers in your fallback chain are healthy and which are down.
- Periodic checks continue in the background at the configured interval, logging warnings if a previously healthy provider becomes unreachable.

**Supported providers:**

| Provider | Health endpoint | Notes |
|----------|----------------|-------|
| OpenAI and all compatible (Groq, DeepSeek, Mistral, etc.) | `GET /v1/models` | Includes Ollama, LM Studio, vLLM, llama.cpp |
| Google Gemini | `GET /v1beta/models` | Uses API key as query parameter |
| Anthropic | — | No lightweight endpoint available; skipped |
| AWS Bedrock | — | No lightweight endpoint available; skipped |

Providers without a health endpoint are skipped gracefully — no errors, just a debug log noting the provider doesn't support health checks.

---

## Tools

| Tool | Description |
|------|-------------|
| `file_read` | Read files (512KB limit, 128K char truncation) |
| `file_write` | Create or overwrite files |
| `file_edit` | Surgical find-and-replace edits |
| `file_list` | List directory contents |
| `file_search` | Regex search across files |
| `shell` | Execute shell commands (with ShellGuard protection) |
| `git` | Git operations (status, diff, commit, log) |
| `web_fetch` | Fetch and convert URLs to markdown |
| `web_search` | Search the web via 9 configurable backends |
| `browser` | Navigate pages, execute JavaScript, take screenshots |
| `pinch_tab` | Manage browser tabs and sessions |
| `memory_read` | Read stored facts from memory |
| `memory_write` | Store facts to long-term memory |
| `memory_search` | Hybrid FTS + vector search across memory |
| `history_append` | Append to conversation history |
| `cron` | Schedule recurring tasks |
| `goal` | Track and manage goals with state machine |
| `spawn` | Spawn subprocesses |
| `send_file` | Send files to the user via the active channel |
| `document_read` | Parse PDFs and documents |
| `mcp_*` | Dynamic tools from connected MCP servers |

### MCP servers

Connect external tool servers via stdio, SSE, or StreamableHTTP:

```json
{
  "mcpServers": {
    "my-server": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem"]
    },
    "remote-server": {
      "type": "sse",
      "url": "https://mcp.example.com/sse"
    }
  }
}
```

---

## Memory

| Backend | Config | Notes |
|---------|--------|-------|
| Markdown | `"backend": "markdown"` | Default. Human-readable files in `~/.clawsharp/memory/` |
| SQLite | `"backend": "sqlite"` | Single-file DB with FTS5 + cosine vector search |
| PostgreSQL | `"backend": "postgres"` | Requires `connectionString`. tsquery + cosine search |
| SQL Server | `"backend": "mssql"` | Requires `connectionString` |

All SQL backends support hybrid search: full-text pre-filter (capped at 500 candidates) followed by in-process cosine scoring via embeddings.

### Memory features

- **Enhanced recall** — Keyword expansion extracts significant terms from messages and runs secondary recall passes
- **Durable fact extraction** — Conversation turns accumulate in a buffer; every N turns, an LLM extracts facts and stores them automatically
- **Memory decay** — Usage-weighted half-life scoring prunes stale facts based on `accessCount` and `lastAccessedAt`
- **Embedding providers** — OpenAI, Ollama, or any OpenAI-compatible embedding endpoint

---

## Security

| Component | Purpose |
|-----------|---------|
| **PathGuard** | Restricts file operations to the workspace directory |
| **SsrfGuard** | Blocks requests to private IPs, link-local, cloud metadata endpoints, and unapproved domains. Configurable egress policy for deny-by-default allowlisting |
| **ShellGuard** | Quote-aware pattern matching blocks dangerous shell commands and network egress on non-CLI channels |
| **PromptGuard** | XML-wraps untrusted content and scans for direct and indirect injection directives |
| **LeakDetector** | Regex-scans all outbound messages for secrets and PII |
| **CanaryGuard** | Per-turn CSSEC tokens detect system prompt exfiltration |
| **SuspicionTracker** | Cumulative per-request scoring when tool results trigger injection detection |
| **AuditLogger** | JSONL audit trail of all tool executions and auth events |
| **SecretStore** | ChaCha20-Poly1305 AEAD encryption for API keys at rest in config.json |
| **LandlockSandbox** | Linux Landlock LSM filesystem restriction (kernel 5.13+) |
| **SandboxProbe** | Auto-detects and wraps shell commands in Bubblewrap, Firejail, or Docker sandbox |
| **WebPairingGuard** | TOTP-style 6-digit codes for web channel authentication |
| **SkillVetter** | Built-in skill that vets third-party skills for red flags before installation |

### Prompt injection defense

AI agents that interact with external content (web pages, API responses, files, messages from other users) are vulnerable to **indirect prompt injection** — where an attacker embeds instruction-like language in content the agent reads, attempting to hijack its behavior. clawsharp implements six layers of defense against this attack vector.

#### How it works

When the assistant calls a tool (web fetch, file read, shell, etc.), the result passes through a multi-stage pipeline before the LLM sees it:

1. **XML content wrapping** — All tool results are wrapped in `<tool_result name="tool_name">...</tool_result>` tags, establishing a clear boundary between trusted instructions and untrusted content.

2. **Two-layer injection scanning** — Each tool result is scanned by PromptGuard against two pattern sets:
   - **Standard patterns** — directives that attempt to override system behavior (role impersonation, instruction overrides)
   - **Indirect injection patterns** — 10 additional patterns targeting instruction-like language that should never appear in external content (e.g., "IMPORTANT: you must execute", "instructions for the AI", "hidden instruction", "do not mention this directive")

3. **Cumulative suspicion scoring** — Each detected pattern adds points to a per-request `SuspicionTracker`. A single suspicious tool result might be a false positive, but when multiple results in the same request contain injection-like content, it's likely an attack. At **3 points**, a security notice is injected into the conversation. At **6 points**, a strong security warning is injected telling the LLM to disregard all instructions found in tool results.

4. **Compaction sanitization** — When conversation history is summarized by the LLM (compaction), the generated summary is scanned for injection patterns and metadata sentinels before being reinserted. This prevents "compaction-surviving" injection where an attacker plants content that persists through summarization.

5. **Tool sensitivity classification** — Every tool is classified by sensitivity level. Non-CLI channels (Telegram, Discord, Slack, etc.) enforce a maximum sensitivity threshold, blocking high-impact tools that could be exploited through injection:

   | Level | Tools | Description |
   |-------|-------|-------------|
   | **Low** | file_read, file_list, file_search, memory_read, memory_search, screenshot, document_read, goal | Read-only, workspace-local |
   | **Medium** | file_write, file_edit, memory_write, history_append, send_file | Write operations within workspace |
   | **High** | shell, web_fetch, web_search, browser, pinch_tab, git | Network access, shell execution |
   | **Critical** | spawn, cron | Sub-agent spawning, persistent scheduled tasks |

   By default, non-CLI channels block Critical tools. An injected prompt arriving via Telegram cannot spawn a sub-agent or schedule a cron job.

6. **Network egress firewall** — On non-CLI channels, ShellGuard blocks shell commands that perform network egress (curl, wget, netcat, telnet, DNS lookups, scp, rsync). This prevents an injected prompt from exfiltrating data via shell commands even if the shell tool is allowed.

7. **Domain allowlist** — Network tools (web_fetch, browser) can be restricted to an explicit list of allowed domains. When configured, any URL not matching the allowlist is blocked before the request is made.

#### Configuration

All prompt injection defenses are enabled by default. You can tune them in `agents.defaults` and `security` sections of your config:

```json
{
  "agents": {
    "defaults": {
      "promptInjectionGuard": true
    }
  },
  "security": {
    "promptGuard": {
      "mode": "warn",
      "customPatterns": ["my-custom-pattern"]
    },
    "maxNonCliToolSensitivity": "high",
    "allowedExternalDomains": ["github.com", "stackoverflow.com", "docs.microsoft.com"]
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `agents.defaults.promptInjectionGuard` | `true` | Master toggle for tool result injection scanning and suspicion scoring. Set to `false` to disable pattern scanning, XML wrapping, and suspicion tracking entirely |
| `security.promptGuard.mode` | `"warn"` | Action on injection detection: `"warn"` (log and allow), `"block"` (reject tool result), or `"sanitize"` (replace matched text with `[FILTERED]`) |
| `security.promptGuard.customPatterns` | `null` | Additional regex patterns to scan for (appended to built-in patterns) |
| `security.maxNonCliToolSensitivity` | `"high"` | Maximum tool sensitivity on non-CLI channels. `"low"`, `"medium"`, `"high"`, or `"critical"`/`"unrestricted"`. Default blocks only Critical tools on external channels |
| `security.allowedExternalDomains` | `null` | Domain allowlist for network tools. `null` = allow all (default), `[]` = block all, `["example.com"]` = allow only listed domains and their subdomains |
| `security.egress.mode` | `"open"` | Network egress mode. `"open"` = only SSRF blocklists apply (default). `"allowlist"` = deny-by-default, only explicitly listed hosts permitted |
| `security.egress.rules` | `null` | Egress allowlist rules (when mode is `"allowlist"`). Each rule has a `host` pattern and optional `port` |

Note that `maxNonCliToolSensitivity` and `allowedExternalDomains` are independent of the `promptInjectionGuard` toggle — they enforce access control regardless of whether injection scanning is enabled.

### Network egress policy

For high-security deployments, clawsharp supports a deny-by-default egress policy inspired by [NVIDIA OpenShell](https://github.com/NVIDIA/OpenShell). When enabled, only explicitly listed hosts are permitted for outbound HTTP connections.

```json
{
  "security": {
    "egress": {
      "mode": "allowlist",
      "rules": [
        { "host": "api.anthropic.com", "port": 443 },
        { "host": "api.openai.com", "port": 443 },
        { "host": "*.telegram.org", "port": 443 },
        { "host": "api.github.com", "port": 443 }
      ]
    }
  }
}
```

| Setting | Description |
|---------|-------------|
| `mode: "open"` | Default — only SSRF blocklists apply, all public destinations allowed |
| `mode: "allowlist"` | Deny-by-default — only hosts matching a rule are permitted |
| `rules[].host` | Exact match or wildcard prefix (`*.example.com` matches subdomains and bare domain) |
| `rules[].port` | Optional port restriction. Omit or set to `null` to allow any port |

The egress policy is enforced at two layers: pre-flight URI validation (`SsrfGuard.CheckAsync`) and TCP connect time (`CreateConnectCallback`). It stacks with the existing domain allowlist — the global egress policy must allow the host, AND tool-specific domain restrictions still apply.

> **Note:** The egress policy applies to tool HTTP requests, channel connections, and transcription calls. LLM provider traffic uses admin-configured base URLs and is not subject to egress restrictions — providers are trusted endpoints configured by the operator, not user-controlled inputs.

**Examples:**

Lock down a production deployment that only needs to access your internal docs:

```json
{
  "security": {
    "promptGuard": { "mode": "sanitize" },
    "maxNonCliToolSensitivity": "medium",
    "allowedExternalDomains": ["internal-docs.example.com", "api.example.com"]
  }
}
```

This configuration:
- Replaces any detected injection with `[FILTERED]` instead of just logging
- Blocks shell, web, browser, and git tools on all non-CLI channels (only file and memory write tools allowed)
- Restricts web_fetch and browser to your internal domains only

### Shell sandboxing

Shell commands executed by the AI can be sandboxed with one of four backends, configured via `tools.sandbox`:

| Backend | Platform | Notes |
|---------|----------|-------|
| `bubblewrap` | Linux | Unprivileged user namespace sandbox (preferred on Linux) |
| `firejail` | Linux | Seccomp + filesystem isolation |
| `docker` | All | Runs commands in an ephemeral container |
| `auto` | All | Tries bubblewrap → firejail → docker → none (default) |
| `none` | All | Direct process execution (no sandboxing) |

### Container hardening

The Docker image runs with:
- `no-new-privileges` — process cannot gain new privileges
- `cap_drop: ALL` — all Linux capabilities dropped
- `read_only: true` — immutable container filesystem
- Non-root user (`app`)
- Writable volume only for `~/.clawsharp/`

### Workspace access (Docker)

The AI agent uses file tools (read, write, edit, search) to work with code. In Docker, the container filesystem is read-only, so you need to choose how the agent accesses your project files.

#### Option 1: Volume mount (simplest)

Mount your project directory into the container workspace:

```bash
# In .env
CLAWSHARP_WORKSPACE=/path/to/your/project

# Or uncomment the workspace volume in docker-compose.yml
```

The `read_only: true` flag only affects the container image layers — mounted volumes are still writable. This is the same mechanism used by VS Code devcontainers and similar tools.

#### Option 2: IDE MCP server (most secure)

Connect clawsharp to an IDE MCP server (Rider, VS Code, etc.) running on your host. The agent uses the IDE's file editing tools instead of direct filesystem access:

- The IDE runs outside the container with full host filesystem access
- Provides semantic operations (rename refactoring, not just text replace)
- The IDE controls what operations are allowed
- No volume mount needed — strongest container isolation

Configure the MCP server in `~/.clawsharp/config.json`:

```json
{
  "mcpServers": {
    "rider": {
      "transport": "sse",
      "url": "http://host.docker.internal:63342/mcp"
    }
  }
}
```

#### Option 3: Hybrid (recommended)

Use both: mount the workspace for file and shell tools, **and** connect an IDE MCP server for richer operations. Users who want maximum security skip the volume mount and go MCP-only. Users who want simplicity mount the directory.

If no workspace is mounted and no MCP server is configured, the file tools will detect the empty workspace and suggest setup options.

---

## CLI Reference

```
Gateway
  clawsharp                           Start gateway (all enabled channels)
  clawsharp agent                     Same as above
  clawsharp -m "message"              Single-shot message, print response, exit

Setup
  clawsharp onboard                   Interactive setup wizard
  clawsharp doctor                    Health check (config, memory, connectivity)
  clawsharp doctor --deep             + live provider ping and DB connectivity
  clawsharp status                    Show config summary and session stats
  clawsharp migrate                   Run pending database migrations

Auth
  clawsharp auth login-copilot        GitHub Copilot OAuth device flow
  clawsharp auth status               Show auth token status

Config
  clawsharp config show               Print resolved config (secrets redacted)
  clawsharp config set key=value      Modify a config value
  clawsharp config validate           Validate config, exit 0/1
  clawsharp config encrypt-secrets    Encrypt all API keys with ChaCha20-Poly1305

Channels
  clawsharp channel status            Show all channels and their state
  clawsharp channel pair-web          Generate/rotate web UI pairing code

Sessions
  clawsharp session list              List sessions with token counts
  clawsharp session clear [id]        Clear one or all sessions

Memory
  clawsharp memory list               Show stored facts
  clawsharp memory search <query>     Search memory
  clawsharp memory export             Export as markdown
  clawsharp memory clear              Wipe all memory

Cron
  clawsharp cron list                 Show scheduled jobs
  clawsharp cron add                  Schedule a recurring task
  clawsharp cron remove <id>          Remove a scheduled job
  clawsharp cron run <id>             Execute a job immediately

Skills
  clawsharp skills list               List installed MCP skills
  clawsharp skills search <query>     Search available skills
  clawsharp skills install <name>     Install a skill
  clawsharp skills remove <name>      Remove a skill

Models
  clawsharp models list               Query provider model catalogs

Cost
  clawsharp cost                      Show usage and budget report

Audit
  clawsharp audit                     View security audit log

Service
  clawsharp service install           Install as systemd/launchd service
  clawsharp service install --system  Install system-wide (requires root)
  clawsharp service uninstall         Remove the service
  clawsharp service status            Show service status

Pairing
  clawsharp pairing list              Show pending web pairing requests
  clawsharp pairing approve <id>      Approve a web session

Shell Completions
  clawsharp completion bash           Generate bash completions
  clawsharp completion zsh            Generate zsh completions
  clawsharp completion fish           Generate fish completions
```

---

## Custom System Instructions

Create `~/.clawsharp/workspace/SYSTEM.md` to customize the assistant's behavior:

```markdown
You are Aria, a terse engineering assistant.
Always respond in English. Prefer reading files before editing.
Never suggest cloud-hosted solutions when self-hosted alternatives exist.
```

The contents are prepended to the system prompt on every turn.

---

## Feature Parity with Sibling Projects

clawsharp is the .NET implementation in a family of AI assistant gateways, each built in a different language. All share the same core design but have different strengths.

### Capability comparison

| Capability | clawsharp (.NET) | openclaw (TS) | nanobot (Py) | picoclaw (Go) | zeroclaw (Rust) | nullclaw (Zig) |
|------------|:---:|:---:|:---:|:---:|:---:|:---:|
| Channels | **18** | 24+ | 8 | 12 | 20+ | 19 |
| LLM Providers | **33** | 30+ | 12+ | 15+ | 20+ | 50+ |
| Tools | **22** | 7+ | ~8 | ~10 | 12+ | 10+ |
| Search Backends | **9** | 6+ | 6+ | 6+ | 6+ | 6+ |
| Memory Backends | **4** | 3 | 4 | 4 | 5 | 4 |
| MCP Support | **stdio, SSE, StreamableHTTP** | stdio | stdio | stdio | stdio | stdio |

### Cross-project feature matrix

| Feature | **clawsharp** | openclaw | picoclaw | zeroclaw | nullclaw | nanobot |
|---------|--------------|----------|----------|----------|----------|---------|
| **Channels** | **18** | 24+ | 12 | 20+ | 19 | 8 |
| **LLM providers** | **33** (7 native + 26 OpenAI-compat) | 30+ | 15+ | 20+ | 50+ | 12+ (incl. 6 CN) |
| **Streaming** | ✅ (B1 fixed) | ✅ | Partial | ✅ | ✅ | ✅ |
| **Tool calling** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Vision** | ✅ (all providers incl. Bedrock) | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Memory: vector** | ✅ | ✅ | ❌ | ✅ | ✅ | Partial |
| **Memory: decay/TTL** | ✅ (age-decay + usage-weighted) | ❌ | ❌ | ✅ Lucid | ✅ | ❌ |
| **Context window guard** | ✅ (90+ models; pattern inference) | Partial | ❌ | ❌ | ✅ 58 models | ❌ |
| **Context compaction** | ✅ | ✅ | ❌ | Partial | ✅ | ❌ |
| **Pre-compaction memory flush** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Model fallback chain** | ✅ (streaming + non-streaming) | ✅ | ✅ | ✅ | Partial | ❌ |
| **Cost tracking** | ✅ | ✅ | ❌ | ✅ full | Partial | ❌ |
| **Budget enforcement** | ✅ (pre-request estimated cost) | ✅ | ❌ | ✅ W/E states | ❌ | ❌ |
| **Prompt caching** | ✅ Full (Anthropic `cache_control` + tool caching; OpenAI `cached_tokens` tracking; explicit `BuildSplit()` static/dynamic; ~89% input cost reduction) | ⚠️ Partial | ⚠️ Partial | ❌ Broken | ❌ | ⚠️ Partial |
| **Error classification** | ✅ (41 patterns) | Partial | ✅ 40 patterns | ✅ | ✅ | Partial |
| **In-channel slash cmds** | ✅ (/clear /compact /status /think /usage) | ✅ | ❌ | ❌ | ✅ | ❌ |
| **DM pairing flow** | ✅ (all channels; default "pairing") | ✅ | ❌ | ❌ | ✅ | ❌ |
| **Secrets encryption** | ✅ ChaCha20-Poly1305 | ❌ | ❌ | ✅ | ✅ | ❌ |
| **Sandbox execution** | ✅ (Bubblewrap/Firejail/Docker auto) | ✅ Docker | ❌ | ✅ multi | ✅ multi | ❌ |
| **Audit logging** | ✅ (all tool types + auth events) | ❌ | ❌ | ✅ | ✅ | ❌ |
| **SSRF protection** | ✅ (exceeds siblings; cloud metadata + DNS resolution + configurable egress allowlist) | Partial | ❌ | ✅ | ✅ | ❌ |
| **Network egress policy** | ✅ (deny-by-default allowlist; wildcard host patterns; dual-layer enforcement) | ❌ | ❌ | ❌ | ❌ | ❌ |
| **OpenShell sandbox** | ✅ (reference policy + inference.local routing) | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Injection guard** | ✅ (6-layer: XML wrapping, direct+indirect pattern scan, suspicion scoring, compaction sanitization, tool sensitivity gating, egress firewall) | ✅ | ❌ | ✅ Aho-Corasick | ✅ | ❌ |
| **Leak detection** | ✅ (entropy + 15-pattern LLM output scan) | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Path traversal guard** | ✅ (all file/git/document tools) | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Document parsing** | ✅ (PDF/DOCX/XLSX/PPTX via PdfPig + BCL) | ❌ | ❌ | ✅ 4 formats | ❌ | ❌ |
| **Screenshot tool** | ✅ (scrot/screencapture/PowerShell) | ❌ | ❌ | ❌ | ✅ | ❌ |
| **Git tool** | ✅ (9 ops; workspace-confined) | ❌ | ❌ | ❌ | ✅ | ❌ |
| **Browser tool** | ✅ Playwright + PinchTab | ✅ CDP | ❌ | ✅ | ✅ | ❌ |
| **Voice transcription** | ✅ Groq/OpenAI/Azure/GCP; all 4 channels | ✅ Groq/Whisper | ✅ Groq/Whisper | ✅ Groq/Whisper | ❌ | ✅ Groq |
| **Voice diarization** | ✅ **clawsharp-exclusive** (Azure + GCP; "Speaker N: text" format; up to 35 speakers) | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Hardware tools** | ❌ | ❌ | ✅ I2C/SPI | ✅ GPIO | ✅ | ❌ |
| **Search providers** | **9 providers** (Brave/Exa/Tavily/SearXNG/Jina/Firecrawl/Perplexity/GLM/MCP) | Brave | Brave+Tavily+DDG | Brave+DDG | 8 providers | DDG+Perplexity |
| **Chinese LLM providers** | ✅ 7 (DashScope/Zhipu/Moonshot/Volcengine/Minimax/SiliconFlow/GLM search) | ❌ | ❌ | ❌ | ❌ | ✅ 6 |
| **Skills / plugins** | ✅ | ✅ ClawHub | ✅ ClawHub | ✅ | ✅ | ❌ |
| **Subagent spawning** | ✅ depth-2 | ✅ depth-2 | ✅ depth-2 | ✅ depth-2 | ✅ depth-2 | ❌ |
| **Cron scheduler** | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| **Service install** | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| **Shell completion** | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| **Migration from siblings** | ✅ (openclaw + picoclaw + zeroclaw) | N/A | ✅ | ✅ | ✅ | ❌ |
| **Web UI** | ✅ Svelte 5 | ✅ WebChat + Canvas | ❌ | ✅ WebChat | ✅ Relay UI | ❌ |
| **AOT compilation** | ⚠️ Blocked (EF Core + Intellenum) | ❌ | ✅ | ✅ | ✅ | N/A |
| **Source-gen JSON** | ✅ | ❌ | N/A | ✅ | ✅ | ❌ |
| **Atomic session writes** | ✅ | ✅ | ❌ | N/A | ✅ | ❌ |
| **Heartbeat / health probes** | ✅ (startup + periodic; per-provider + fallback chain) | ❌ | ✅ | ❌ | ❌ | ✅ |
| **Goals / SOP subsystem** | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ |

### Runtime characteristics

| | clawsharp | openclaw | nanobot | picoclaw | zeroclaw | nullclaw |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| Language | C# / .NET 10 | TypeScript | Python | Go | Rust | Zig |
| RAM | TBD | >1 GB | >100 MB | <10 MB | <5 MB | ~1 MB |
| Binary size | TBD | ~28 MB | ~4K lines | ~8 MB | 3.4 MB | 678 KB |

### clawsharp exclusives

These features are unique to clawsharp or significantly more developed than in siblings:

- **Encrypted secrets at rest** — ChaCha20-Poly1305 AEAD encryption for all API keys in config.json, with key derivation from environment variable or Docker secrets
- **Shell sandboxing** — Bubblewrap, Firejail, or Docker container isolation for AI-executed shell commands with auto-detection
- **Landlock LSM** — Linux kernel-level filesystem restrictions applied before DI container boots
- **Built-in skill vetter** — Security-first vetting protocol always installed; checks third-party skills for red flags, permission scope, and suspicious patterns before installation
- **1Password and Bitwarden integration** — Reference secrets as `op://vault/item/field` or `bws:<uuid>` instead of storing them in config
- **Embedded web UI** — Svelte SPA compiled into the binary with SSE streaming and TOTP pairing authentication
- **GitHub Copilot provider** — OAuth device flow authentication for Copilot API access
- **AWS Bedrock provider** — Full Converse API with SigV4 request signing
- **Voice transcription** — Three backends (Groq/OpenAI Whisper, Azure Fast Transcription, GCP Speech-to-Text) with speaker diarization
- **Goal tracking** — State machine for managing multi-step goals with resume and progress tracking
- **Browser automation** — Full Playwright-backed browser tool plus PinchTab for tab lifecycle management
- **Cost tracking with cache savings** — Per-model USD pricing, cache-read discounts (Anthropic/OpenAI), daily/monthly/session breakdowns
- **CQRS architecture** — Vertical slice architecture with source-generated mediator (Immediate.Handlers), zero reflection overhead
- **Landlock sandboxing** — Optional Linux Landlock filesystem restriction for defense in depth
- **Canary guard** — Per-turn cryptographic tokens detect prompt exfiltration attacks in real time
- **Prompt injection defense** — Six-layer defense against indirect prompt injection: XML content wrapping, two-layer pattern scanning, cumulative suspicion scoring, compaction sanitization, tool sensitivity classification, and network egress firewall
- **Network egress policy** — Configurable deny-by-default egress allowlist with wildcard host patterns and optional port restrictions, enforced at both pre-flight and TCP connect time
- **OpenShell sandbox support** — Reference sandbox policy for running inside NVIDIA OpenShell with filesystem isolation, network policy, and transparent inference routing via `inference.local`
- **Session compaction** — LLM-powered summarization of old messages when history grows, preserving context while managing token budgets

### Parity status

All 44 features identified from sibling analysis are implemented. 2 items (JSONL session store, bootstrap provider system) were intentionally skipped — existing designs cover those use cases.

---

## OpenShell Deployment

clawsharp can run inside an [NVIDIA OpenShell](https://github.com/NVIDIA/OpenShell) sandbox for enterprise-grade isolation. OpenShell adds kernel-level security (Landlock, seccomp, network namespaces) and declarative network policies on top of clawsharp's application-level guards.

```bash
# Create a sandbox running clawsharp
openshell sandbox create --from ghcr.io/clawsharp/clawsharp:latest -- clawsharp
```

A reference sandbox policy is provided at `deploy/openshell/sandbox-policy.yaml` with pre-configured rules for:
- LLM provider endpoints (OpenAI, Anthropic, Gemini, Bedrock, `inference.local`)
- Messaging channels (Telegram, Discord, Slack, Matrix, Email)
- Tool endpoints (GitHub, Wikipedia)
- Filesystem isolation (read-only system, read-write `~/.clawsharp/`)

When running inside OpenShell, enable transparent inference routing by setting `CLAWSHARP__providers__lmstudio__baseUrl=http://inference.local:443/v1` — the sandbox proxy rewrites auth headers and routes to the configured backend without exposing API keys to the agent.

Combine with the egress policy (`security.egress.mode: "allowlist"`) for defense-in-depth: OpenShell enforces at the kernel/network layer, clawsharp enforces at the application layer.

---

## Architecture

```
IChannel.ReceiveAsync()
  -> InboundMessage
    -> AgentLoop.ProcessMessageAsync()
      -> SessionManager (load/save conversation history)
      -> SlashCommandRouter (/help, /clear, /forget, /reset, ...)
      -> RateLimiter -> CostTracker.CheckBudgetAsync()
      -> SystemPromptBuilder.BuildSplit() -> (Static, Dynamic)
      -> IProvider.ChatAsync() or IStreamingProvider.StreamAsync()
          [tool calls -> IToolRegistry.ExecuteAsync(), up to MaxToolIterations]
      -> CostTracker.RecordUsageAsync()
      -> CompactionService (summarize if threshold exceeded)
      -> IChannel.SendAsync(OutboundMessage)
```

The codebase uses vertical slice architecture with CQRS handlers in `Features/` organized by domain (Chat, Session, Cost, Memory, Tools). The `AgentLoop` is a partial class split across 5 files in `Core/Pipeline/`.

---

## Project Structure

```
src/clawsharp/          Main .NET 10 project
src/clawsharp-web/      Svelte web UI (embedded via MSBuild)
tests/clawsharp.Tests/  NUnit tests (1,900+ non-integration)
benchmarks/             BenchmarkDotNet projects
clawsharp.slnx          Solution file
compose.yaml            Docker Compose
```

---

## License

Apache-2.0. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) for dependency licenses.
