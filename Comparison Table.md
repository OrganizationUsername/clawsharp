## 5. Cross-Project Feature Matrix

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
| **Prompt caching** | ✅ Full (Anthropic `cache_control` + tool caching; OpenAI `cached_tokens` tracking; explicit `BuildSplit()` static/dynamic; ~89% input cost reduction) | ⚠️ Partial (no datetime in system prompt ✅; `cache_control` for OpenRouter/Anthropic ✅; reads `cached_tokens` ✅; no explicit static/dynamic split; delegates direct Anthropic to pi-ai) | ⚠️ Partial (explicit static/dynamic split ✅; Anthropic `cache_control` ✅; missing OpenAI `cached_tokens` read-back) | ❌ Broken (has `cache_control` infrastructure but datetime mid-prompt position 6/8 causes 100% cache miss every turn) | ❌ Not implemented (no `cache_control`, no split; datetime mid-prompt) | ⚠️ Partial (datetime as separate user message ✅; LiteLLM `cache_control` ✅; CustomProvider has no caching; `cached_tokens` not read back; Codex cache key includes volatile runtime context) |
| **Error classification** | ✅ (41 patterns) | Partial | ✅ 40 patterns | ✅ | ✅ | Partial |
| **In-channel slash cmds** | ✅ (/clear /compact /status /think /usage) | ✅ | ❌ | ❌ | ✅ | ❌ |
| **DM pairing flow** | ✅ (all channels; default "pairing") | ✅ | ❌ | ❌ | ✅ | ❌ |
| **Secrets encryption** | ✅ ChaCha20-Poly1305 | ❌ | ❌ | ✅ | ✅ | ❌ |
| **Sandbox execution** | ✅ (Bubblewrap/Firejail/Docker auto) | ✅ Docker | ❌ | ✅ multi | ✅ multi | ❌ |
| **Audit logging** | ✅ (all tool types + auth events) | ❌ | ❌ | ✅ | ✅ | ❌ |
| **SSRF protection** | ✅ (exceeds siblings; cloud metadata + DNS resolution) | Partial | ❌ | ✅ | ✅ | ❌ |
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
| **AOT compilation** | ✅ | ❌ | ✅ | ✅ | ✅ | N/A |
| **Source-gen JSON** | ✅ | ❌ | N/A | ✅ | ✅ | ❌ |
| **Atomic session writes** | ✅ | ✅ | ❌ | N/A | ✅ | ❌ |
| **Heartbeat / health probes** | ✅ (startup + periodic; per-provider + fallback chain) | ❌ | ✅ | ❌ | ❌ | ✅ |
| **Goals / SOP subsystem** | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ |
