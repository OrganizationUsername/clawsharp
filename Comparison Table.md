## 5. Cross-Project Feature Matrix

| Feature | openclaw | picoclaw | zeroclaw | nullclaw | nanobot | **clawsharp** |
|---------|----------|----------|----------|----------|---------|--------------|
| **Channels** | 24+ | 12 | 20+ | 19 | 8 | **18** |
| **LLM providers** | 30+ | 15+ | 20+ | 50+ | 12+ (incl. 6 CN) | **33** (7 native + 26 OpenAI-compat) |
| **Streaming** | ✅ | Partial | ✅ | ✅ | ✅ | ✅ (B1 fixed) |
| **Tool calling** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Vision** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ (all providers incl. Bedrock) |
| **Memory: vector** | ✅ | ❌ | ✅ | ✅ | Partial | ✅ |
| **Memory: decay/TTL** | ❌ | ❌ | ✅ Lucid | ✅ | ❌ | ✅ (age-decay + usage-weighted) |
| **Context window guard** | Partial | ❌ | ❌ | ✅ 58 models | ❌ | ✅ (90+ models; pattern inference) |
| **Context compaction** | ✅ | ❌ | Partial | ✅ | ❌ | ✅ |
| **Pre-compaction memory flush** | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ (awaited; race fixed) |
| **Model fallback chain** | ✅ | ✅ | ✅ | Partial | ❌ | ✅ (streaming + non-streaming) |
| **Cost tracking** | ✅ | ❌ | ✅ full | Partial | ❌ | ✅ |
| **Budget enforcement** | ✅ | ❌ | ✅ W/E states | ❌ | ❌ | ✅ (pre-request estimated cost) |
| **Prompt caching** | ⚠️ Partial (no datetime in system prompt ✅; `cache_control` for OpenRouter/Anthropic ✅; reads `cached_tokens` ✅; no explicit static/dynamic split; delegates direct Anthropic to pi-ai) | ⚠️ Partial (explicit static/dynamic split ✅; Anthropic `cache_control` ✅; missing OpenAI `cached_tokens` read-back) | ❌ Broken (has `cache_control` infrastructure but datetime mid-prompt position 6/8 causes 100% cache miss every turn) | ❌ Not implemented (no `cache_control`, no split; datetime mid-prompt) | ⚠️ Partial (datetime as separate user message ✅; LiteLLM `cache_control` ✅; CustomProvider has no caching; `cached_tokens` not read back; Codex cache key includes volatile runtime context) | ✅ Full (Anthropic `cache_control` + tool caching; OpenAI `cached_tokens` tracking; explicit `BuildSplit()` static/dynamic; ~89% input cost reduction) |
| **Error classification** | Partial | ✅ 40 patterns | ✅ | ✅ | Partial | ✅ (41 patterns) |
| **In-channel slash cmds** | ✅ | ❌ | ❌ | ✅ | ❌ | ✅ (/clear /compact /status /think /usage) |
| **DM pairing flow** | ✅ | ❌ | ❌ | ✅ | ❌ | ✅ (all channels; default "pairing") |
| **Secrets encryption** | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ (ChaCha20-Poly1305; decrypt wired) |
| **Sandbox execution** | ✅ Docker | ❌ | ✅ multi | ✅ multi | ❌ | ✅ (Bubblewrap/Firejail/Docker auto) |
| **Audit logging** | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ (all tool types + auth events) |
| **SSRF protection** | Partial | ❌ | ✅ | ✅ | ❌ | ✅ (exceeds siblings; cloud metadata + DNS resolution) |
| **Injection guard** | ✅ | ❌ | ✅ Aho-Corasick | ✅ | ❌ | ✅ (6-layer: XML wrapping, direct+indirect pattern scan, suspicion scoring, compaction sanitization, tool sensitivity gating, egress firewall) |
| **Leak detection** | ❌ | ❌ | ✅ | ❌ | ❌ | ✅ (entropy + 15-pattern LLM output scan) |
| **Path traversal guard** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ (all file/git/document tools) |
| **Document parsing** | ❌ | ❌ | ✅ 4 formats | ❌ | ❌ | ✅ (PDF/DOCX/XLSX/PPTX via PdfPig + BCL) |
| **Screenshot tool** | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ (scrot/screencapture/PowerShell) |
| **Git tool** | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ (9 ops; workspace-confined) |
| **Browser tool** | ✅ CDP | ❌ | ✅ | ✅ | ❌ | ✅ Playwright + PinchTab |
| **Voice transcription** | ✅ Groq/Whisper | ✅ Groq/Whisper | ✅ Groq/Whisper | ❌ | ✅ Groq | ✅ Groq/OpenAI/Azure/GCP; all 4 channels |
| **Voice diarization** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ **clawsharp-exclusive** (Azure + GCP; "Speaker N: text" format; up to 35 speakers) |
| **Hardware tools** | ❌ | ✅ I2C/SPI | ✅ GPIO | ✅ | ❌ | ❌ |
| **Search providers** | Brave | Brave+Tavily+DDG | Brave+DDG | 8 providers | DDG+Perplexity | **9 providers** (Brave/Exa/Tavily/SearXNG/Jina/Firecrawl/Perplexity/GLM/MCP) |
| **Chinese LLM providers** | ❌ | ❌ | ❌ | ❌ | ✅ 6 | ✅ 7 (DashScope/Zhipu/Moonshot/Volcengine/Minimax/SiliconFlow/GLM search) |
| **Skills / plugins** | ✅ ClawHub | ✅ ClawHub | ✅ | ✅ | ❌ | ✅ |
| **Subagent spawning** | ✅ depth-2 | ✅ depth-2 | ✅ depth-2 | ✅ depth-2 | ❌ | ✅ depth-2 |
| **Cron scheduler** | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| **Service install** | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| **Shell completion** | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| **Migration from siblings** | N/A | ✅ | ✅ | ✅ | ❌ | ✅ (openclaw + picoclaw + zeroclaw) |
| **Web UI** | ✅ WebChat + Canvas | ❌ | ✅ WebChat | ✅ Relay UI | ❌ | ✅ Svelte 5 |
| **AOT compilation** | ❌ | ✅ | ✅ | ✅ | N/A | ✅ (SelfContained JIT; EnableConfigurationBindingGenerator unblocks P5.6 NativeAOT publish) |
| **Source-gen JSON** | ❌ | N/A | ✅ | ✅ | ❌ | ✅ (25 JsonSerializerContext classes) |
| **Atomic session writes** | ✅ | ❌ | N/A | ✅ | ❌ | ✅ (File.Move atomic rename) |
| **Heartbeat / health probes** | ❌ | ✅ | ❌ | ❌ | ✅ | ✅ (startup + periodic; per-provider + fallback chain) |
| **Goals / SOP subsystem** | ❌ | ❌ | ✅ | ❌ | ❌ | ✅ (GoalTool state machine; resume + progress tracking) |