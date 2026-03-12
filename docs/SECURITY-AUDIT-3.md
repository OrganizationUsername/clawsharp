# Security Audit #3 — Replit Checklist Gap Analysis

**Date:** 2026-03-11
**Source:** Replit "Security checklist for vibe coding" applied against clawsharp codebase
**Scope:** Front-end security, back-end security, ongoing security practices

---

## Summary

clawsharp's security posture is well ahead of what Replit's checklist covers — three prior
audit rounds, prompt injection defenses, tool sensitivity classification, and secret encryption
go far beyond the basics. This audit identified **14 gaps** across 3 severity tiers.

---

## Already Solid (No Action Needed)

| Replit Item | clawsharp Status |
|---|---|
| HTTPS everywhere | Self-hosted; TLS optional, documented as reverse-proxy concern |
| Input sanitization (XSS) | CSP with per-request cryptographic nonce, PromptGuard XML-wraps untrusted content |
| Sensitive data out of browser | No secrets client-side; bearer tokens only, no localStorage |
| API keys in frontend | All secrets server-side with ChaCha20-Poly1305 encryption at rest |
| Authentication | WebPairingGuard (TOTP 6-digit + bearer token), constant-time comparison, brute-force lockout |
| Authorization checks | AllowFrom semantics, ToolSensitivity per-channel enforcement |
| SQL injection prevention | EF Core throughout, parameterized queries only |
| Rate limiting | Per-session sliding window (20 req/60s default), configurable |
| Secure cookies | Not applicable — no cookies used, entirely bearer-token auth |
| DDoS protection | Rate limiter + 1MB request body limit on webhooks + WebSocket message limits |
| No hardcoded secrets | Zero found; 1Password + Bitwarden integration, .env in .gitignore |

---

## Gaps — High Priority

### H-01: No CI/CD or Dependency Scanning

**Risk:** Vulnerable dependencies ship undetected.

No GitHub Actions, no `dotnet audit`, npm uses `--no-audit` flag in the MSBuild target.
Zero automated vulnerability scanning exists today.

**Files:** `src/clawsharp/clawsharp.csproj` (line 79: `--no-audit`)

**Fix:** Add a GitHub Actions workflow with `dotnet audit` and `npm audit` steps.
Consider `dotnet list package --vulnerable` as a simpler first step.

---

### H-02: Error Messages Leak Internal Details

**Risk:** Tool results expose file paths, connection errors, Playwright internals, and
library-specific error messages. An LLM could be prompted to trigger errors and exfiltrate
system structure.

~25+ locations return `ex.Message` directly to users via tool result strings.
`AgentLoop.cs` itself is safe ("Sorry, something went wrong") but individual tools are not.

**Affected tools (non-exhaustive):**

| Tool | File | Lines |
|---|---|---|
| FileReadTool | `Tools/Files/FileReadTool.cs` | 57, 88 |
| FileWriteTool | `Tools/Files/FileWriteTool.cs` | 59, 71 |
| FileEditTool | `Tools/Files/FileEditTool.cs` | 66, 81 |
| FileListTool | `Tools/Files/FileListTool.cs` | 49, 64 |
| FileSearchTool | `Tools/Files/FileSearchTool.cs` | 54, 69 |
| WebFetchTool | `Tools/Web/WebFetchTool.cs` | 103 |
| WebSearchTool | `Tools/Web/WebSearchTool.cs` | 152 |
| BrowserTool | `Tools/Browser/BrowserTool.cs` | 185, 189, 194 |
| PinchTabTool | `Tools/Browser/PinchTabTool.cs` | 130, 135 |
| ScreenshotTool | `Tools/Browser/ScreenshotTool.cs` | 116 |
| SendFileTool | `Tools/Ops/SendFileTool.cs` | 72, 112 |
| GitTool | `Tools/Ops/GitTool.cs` | 97, 196 |
| DocumentReadTool | `Tools/Ops/DocumentReadTool.cs` | 74, 103 |
| SpawnTool | `Tools/Ops/SpawnTool.cs` | 184, 244 |
| ToolRegistry | `Tools/ToolRegistry.cs` | 273 |
| McpClient | `Tools/Mcp/McpClient.cs` | 172 |
| SlashCommandRouter | `Core/Pipeline/AgentLoop.SlashCommands.cs` | 149, 176 |

**Fix:** Replace `ex.Message` with generic error strings in tool return values.
Log the full exception server-side. Pattern:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Tool '{Name}' failed", Name);
    return "Error: operation failed. Check server logs for details.";
}
```

---

### H-03: Signal Voice Attachment — No Size Pre-Check

**Risk:** OOM via oversized Base64 attachment decoded without size validation.

**File:** `Channels/Signal/SignalChannel.cs` — `TranscribeAttachmentAsync()`

**Fix:** Check `base64.Length * 3 / 4 > ClawsharpConstants.MaxVoiceFileBytes` before decoding.

---

### H-04: WebSocket Origin Header Not Validated

**Risk:** Cross-origin WebSocket hijacking. HTTP CORS is solid, but the `/ws` upgrade path
bypasses origin checks entirely. First-frame auth mitigates but doesn't eliminate the vector.

**File:** `Channels/Web/WebChannel.cs` — WebSocket upgrade handler (lines 136-147)

**Fix:** Validate `Origin` header against `_allowedOrigins` before `AcceptWebSocketAsync()`.

---

## Gaps — Medium Priority

### M-01: Missing HSTS Header

**Risk:** Downgrade attacks when TLS is enabled directly (not via reverse proxy).

`Strict-Transport-Security` not set even when `_tls: true`.

**File:** `Channels/Web/WebChannel.cs` — `ApplySecurityHeaders()`

**Fix:** Add `Strict-Transport-Security: max-age=31536000; includeSubDomains` when `_tls` is true.

---

### M-02: Missing Permissions-Policy Header

**Risk:** Browser APIs (camera, microphone, geolocation) available by default.

**File:** `Channels/Web/WebChannel.cs` — `ApplySecurityHeaders()`

**Fix:** Add `Permissions-Policy: camera=(), microphone=(), geolocation=(), usb=(), payment=()`.

---

### M-03: FileWriteTool Has No Size Limit

**Risk:** Disk exhaustion if LLM generates multi-GB output.

**File:** `Tools/Files/FileWriteTool.cs`

**Fix:** Add configurable max file size per write (e.g., 10 MB default).

---

### M-04: Slack File Upload — No Size Limit

**Risk:** Bandwidth/quota exhaustion; API errors if workspace limit exceeded.

**File:** `Channels/Slack/SlackChannel.cs` — `SendFileAsync()`

**Fix:** Add explicit size check (e.g., 100 MB cap) before `UploadFileAsync()`.

---

### M-05: WhatsApp/Discord Voice — No Size Pre-Check

**Risk:** Size only enforced downstream by Whisper API, not before download.

**Files:**
- `Channels/WhatsApp/WhatsAppChannel.cs` — `TranscribeWhatsAppAudioAsync()`
- `Channels/Discord/DiscordMessageResponder.cs` — `TranscribeVoiceAttachmentAsync()`

**Fix:** Add `MaxVoiceFileBytes` check before downloading audio content.

---

### M-06: Rate Limiting Is Per-Session, Not Per-IP

**Risk:** Attacker can bypass rate limits by creating multiple sessions.

**File:** `Core/Services/RateLimiter.cs`

**Fix:** Add optional per-IP rate limiting layer (secondary to per-session).

---

### M-07: DocumentReadTool — Extension-Only Validation

**Risk:** Malformed files could cause parser crashes or unexpected behavior.

**File:** `Tools/Ops/DocumentReadTool.cs`

**Fix:** Add magic-byte MIME validation (check file header matches claimed extension).

---

## Gaps — Low Priority

### L-01: Slash Commands Lack Input Length Limits

**Risk:** Low — arguments feed into handlers, not shell/SQL. Theoretically could cause
excessive memory use with very long arguments.

**File:** `Core/Pipeline/SlashCommandRouter.cs`

**Fix:** Add max argument length (e.g., 10,000 chars) before handler dispatch.

---

### L-02: No X-XSS-Protection Header

**Risk:** Minimal — deprecated in modern browsers but useful as legacy fallback.

**File:** `Channels/Web/WebChannel.cs` — `ApplySecurityHeaders()`

**Fix:** Add `X-XSS-Protection: 1; mode=block`.

---

### L-03: Audio Downloads Lack Explicit HTTP Timeouts

**Risk:** Slow Loris style hang on malicious slow responses.

**Files:** Telegram, Discord, Mattermost channel audio download paths.

**Fix:** Ensure named HttpClients have explicit 30s read timeouts configured.

---

## Implementation Checklist

- [ ] H-01: Add GitHub Actions CI with `dotnet audit` + `npm audit`
- [ ] H-02: Sanitize error messages across all 17+ tool files
- [ ] H-03: Signal voice size pre-check
- [ ] H-04: WebSocket origin validation
- [ ] M-01: HSTS header when TLS enabled
- [ ] M-02: Permissions-Policy header
- [ ] M-03: FileWriteTool size limit
- [ ] M-04: Slack file upload size limit
- [ ] M-05: WhatsApp/Discord voice size pre-check
- [ ] M-06: Per-IP rate limiting layer
- [ ] M-07: DocumentReadTool magic-byte validation
- [ ] L-01: Slash command argument length limit
- [ ] L-02: X-XSS-Protection header
- [ ] L-03: Audio download HTTP timeouts
