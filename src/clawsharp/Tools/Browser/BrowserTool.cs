using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawsharp.Config;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Clawsharp.Config.Features;

namespace Clawsharp.Tools.Browser;

/// <summary>
/// Browser automation tool using Microsoft Playwright (Chromium).
/// Provides 8 actions: navigate, snapshot, click, type, select, screenshot, evaluate, close.
///
/// Uses ARIA accessibility snapshots as the primary page representation for LLM interaction.
/// Elements are targeted via Playwright role-based locators built from the ARIA snapshot data.
///
/// NOTE: Playwright internally uses reflection and a Node.js driver process. This is
/// inherently incompatible with NativeAOT. If AOT compilation is re-enabled in the future,
/// this tool will need to be conditionally excluded or the Playwright team will need to
/// ship an AOT-compatible driver. See: https://github.com/microsoft/playwright-dotnet/issues/2770
/// </summary>
public sealed partial class BrowserTool(
    BrowserSessionCache sessions,
    IOptions<AppConfig> configOptions,
    AuditLogger? auditLogger,
    ILogger<BrowserTool> logger)
    : Tool
{
    private const int NavigationTimeoutMs = 30_000;

    private const int OperationTimeoutMs = 10_000;

    private const int SubmitTimeoutMs = 5_000;

    private const int MaxSnapshotChars = 16_000;

    private const int MaxEvalOutputChars = 8_000;

    /// <summary>
    /// JavaScript API patterns blocked from evaluation to prevent cookie theft,
    /// storage exfiltration, cross-origin communication, and credential access.
    /// Checked case-insensitively against the full expression text.
    /// </summary>
    private static readonly string[] BlockedJsPatterns =
    [
        "document.cookie",
        "localStorage",
        "sessionStorage",
        "XMLHttpRequest",
        "importScripts",
        "WebSocket",
        "navigator.sendBeacon",
        "ServiceWorker",
        "crypto.subtle",
        ".credentials",
        "window.opener",
        "postMessage",
        // CRIT-03: Block SSRF via fetch/navigation and other dangerous APIs
        "fetch(",
        "window.location",
        "document.location",
        "location.href",
        "location.assign",
        "location.replace",
        "navigator.clipboard",
        "eval(",
        "Function(",
        "setTimeout(",
        "setInterval(",
    ];

    private readonly BrowserConfig _config = configOptions.Value.Tools.Browser;

    /// <summary>Session ID read from the per-async-flow AsyncLocal in ToolRegistry.</summary>
    public string? SessionId => ToolRegistry.CurrentSessionId;

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "browser";

    public override ToolSensitivity Sensitivity => ToolSensitivity.High;

    public override string Description =>
        "Automate a web browser: navigate pages, take accessibility snapshots, click elements, " +
        "type text, select options, take screenshots, evaluate JavaScript, and close sessions. " +
        "Use 'snapshot' after navigation to see the page's accessibility tree.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "action": {
                                                         "type": "string",
                                                         "enum": ["navigate", "snapshot", "click", "type", "select", "screenshot", "evaluate", "close"],
                                                         "description": "The browser action to perform"
                                                       },
                                                       "url": {
                                                         "type": "string",
                                                         "description": "URL to navigate to (for 'navigate' action)"
                                                       },
                                                       "selector": {
                                                         "type": "string",
                                                         "description": "CSS/ARIA selector to scope the snapshot (for 'snapshot' action, default 'body')"
                                                       },
                                                       "ref": {
                                                         "type": "string",
                                                         "description": "Element reference ID from the ARIA snapshot, e.g. 'e5' (for 'click', 'type', 'select' actions)"
                                                       },
                                                       "text": {
                                                         "type": "string",
                                                         "description": "Text to type into the element (for 'type' action)"
                                                       },
                                                       "submit": {
                                                         "type": "boolean",
                                                         "description": "Whether to press Enter after typing (for 'type' action, default false)"
                                                       },
                                                       "values": {
                                                         "type": "array",
                                                         "items": { "type": "string" },
                                                         "description": "Values to select (for 'select' action)"
                                                       },
                                                       "fullPage": {
                                                         "type": "boolean",
                                                         "description": "Whether to capture a full-page screenshot (for 'screenshot' action, default false)"
                                                       },
                                                       "expression": {
                                                         "type": "string",
                                                         "description": "JavaScript expression to evaluate (for 'evaluate' action, requires config.evaluateEnabled)"
                                                       }
                                                     },
                                                     "required": ["action"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return "Error: browser tool is disabled in configuration.";
        }

        var action = arguments.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        var sessionId = SessionId ?? "default";

        try
        {
            return action switch
            {
                "navigate" => await NavigateAsync(arguments, sessionId, ct),
                "snapshot" => await SnapshotAsync(arguments, sessionId, ct),
                "click" => await ClickAsync(arguments, sessionId, ct),
                "type" => await TypeAsync(arguments, sessionId, ct),
                "select" => await SelectAsync(arguments, sessionId, ct),
                "screenshot" => await ScreenshotAsync(arguments, sessionId, ct),
                "evaluate" => await EvaluateAsync(arguments, sessionId, ct),
                "close" => await CloseAsync(sessionId),
                _ => $"Error: unknown browser action '{action}'. " +
                     "Valid actions: navigate, snapshot, click, type, select, screenshot, evaluate, close.",
            };
        }
        catch (PlaywrightException ex)
        {
            LogPlaywrightError(logger, ex, action);
            return "Error: browser operation failed.";
        }
        catch (TimeoutException)
        {
            return "Error: operation timed out.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpectedBrowserError(logger, ex, action);
            return "Error: operation failed.";
        }
    }

    private async Task<string> NavigateAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var url = args.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Error: 'url' is required for the 'navigate' action.";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"Error: invalid URL: {url}";
        }

        // SSRF check
        var ssrfError = await SsrfGuard.CheckAsync(uri, ct).ConfigureAwait(false);
        if (ssrfError is not null)
        {
            if (auditLogger is not null)
            {
                _ = auditLogger.LogPolicyViolationAsync(
                                    $"SSRF blocked browser navigation: {url}", ChannelName, ssrfBlocked: true, ct: ct)
                                .LogExceptions(logger, "audit:browser_ssrf");
            }

            return ssrfError;
        }

        // Domain allowlist check
        var domainError = BrowserNavigationGuard.CheckAllowedDomain(uri, _config.AllowedDomains);
        if (domainError is not null)
        {
            return domainError;
        }

        // Audit log
        if (auditLogger is not null)
        {
            _ = auditLogger.LogFileAccessAsync(url, "browser_navigate", ChannelName, success: true, ct: ct)
                            .LogExceptions(logger, "audit:browser_navigate");
        }

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);
        var response = await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = NavigationTimeoutMs,
        }).ConfigureAwait(false);

        var title = await page.TitleAsync().ConfigureAwait(false);
        var status = response?.Status ?? 0;

        return $"Navigated to {url}. Status: {status}. Title: {title}";
    }

    private async Task<string> SnapshotAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var selector = args.TryGetProperty("selector", out var s) ? s.GetString() ?? "body" : "body";

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);

        // Inject ref annotations into interactive elements and capture an ARIA snapshot
        // with [ref=eNN] markers. This mirrors what the Playwright MCP server does internally.
        var snapshot = await CaptureAnnotatedSnapshotAsync(page, selector).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return "Snapshot returned empty content. The page may not have loaded yet.";
        }

        // Cap output size to avoid blowing up the LLM context
        if (snapshot.Length > MaxSnapshotChars)
        {
            snapshot = snapshot[..MaxSnapshotChars] + "\n... (truncated)";
        }

        return snapshot;
    }

    private async Task<string> ClickAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var refId = args.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(refId))
        {
            return "Error: 'ref' is required for the 'click' action. Use 'snapshot' first to get element refs.";
        }

        if (ValidateRefId(refId) is { } refError)
        {
            return refError;
        }

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);
        var locator = page.Locator($"[data-pw-ref='{refId}']");

        if (await locator.CountAsync().ConfigureAwait(false) == 0)
        {
            return $"Error: no element found with ref '{refId}'. Run 'snapshot' to refresh element refs.";
        }

        await locator.First.ClickAsync(new LocatorClickOptions { Timeout = OperationTimeoutMs }).ConfigureAwait(false);
        return $"Clicked element [ref={refId}].";
    }

    private async Task<string> TypeAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var refId = args.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
        var text = args.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var submit = args.TryGetProperty("submit", out var sb) && sb.GetBoolean();

        if (string.IsNullOrWhiteSpace(refId))
        {
            return "Error: 'ref' is required for the 'type' action.";
        }

        if (ValidateRefId(refId) is { } refError)
        {
            return refError;
        }

        if (string.IsNullOrEmpty(text))
        {
            return "Error: 'text' is required for the 'type' action.";
        }

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);
        var locator = page.Locator($"[data-pw-ref='{refId}']");

        if (await locator.CountAsync().ConfigureAwait(false) == 0)
        {
            return $"Error: no element found with ref '{refId}'. Run 'snapshot' to refresh element refs.";
        }

        await locator.First.FillAsync(text, new LocatorFillOptions { Timeout = OperationTimeoutMs }).ConfigureAwait(false);

        if (submit)
        {
            await locator.First.PressAsync("Enter", new LocatorPressOptions { Timeout = SubmitTimeoutMs }).ConfigureAwait(false);
        }

        return $"Typed into element [ref={refId}].{(submit ? " Submitted with Enter." : "")}";
    }

    private async Task<string> SelectAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var refId = args.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
        string[] values = [];
        if (args.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array)
        {
            values = v.EnumerateArray()
                      .Select(e => e.GetString() ?? "")
                      .Where(s => s.Length > 0)
                      .ToArray();
        }

        if (string.IsNullOrWhiteSpace(refId))
        {
            return "Error: 'ref' is required for the 'select' action.";
        }

        if (ValidateRefId(refId) is { } refError)
        {
            return refError;
        }

        if (values.Length == 0)
        {
            return "Error: 'values' array is required and must contain at least one option.";
        }

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);
        var locator = page.Locator($"[data-pw-ref='{refId}']");

        if (await locator.CountAsync().ConfigureAwait(false) == 0)
        {
            return $"Error: no element found with ref '{refId}'. Run 'snapshot' to refresh element refs.";
        }

        await locator.First.SelectOptionAsync(values, new LocatorSelectOptionOptions { Timeout = OperationTimeoutMs })
                     .ConfigureAwait(false);
        return $"Selected {values.Length} option(s) in element [ref={refId}].";
    }

    private async Task<string> ScreenshotAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var fullPage = args.TryGetProperty("fullPage", out var fp) && fp.GetBoolean();

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = fullPage,
            Type = ScreenshotType.Png,
        }).ConfigureAwait(false);

        // Save to workspace temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-screenshots");
        Directory.CreateDirectory(tempDir);
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var filename = $"browser-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{shortId}.png";
        var path = Path.Combine(tempDir, filename);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

        return $"Screenshot saved to {path} ({bytes.Length:N0} bytes).";
    }

    private async Task<string> EvaluateAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        if (!_config.EvaluateEnabled)
        {
            return "Error: JavaScript evaluation is disabled in configuration (tools.browser.evaluateEnabled = false). " +
                   "Enable it only if you trust the LLM's output.";
        }

        var expression = args.TryGetProperty("expression", out var e) ? e.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "Error: 'expression' is required for the 'evaluate' action.";
        }

        // Check for dangerous JavaScript APIs before execution
        foreach (var blocked in BlockedJsPatterns)
        {
            if (expression.Contains(blocked, StringComparison.OrdinalIgnoreCase))
            {
                if (auditLogger is not null)
                {
                    _ = auditLogger.LogPolicyViolationAsync(
                                        $"Blocked JS API in browser evaluate: '{blocked}' in expression: {expression}",
                                        ChannelName, ssrfBlocked: false, ct: ct)
                                    .LogExceptions(logger, "audit:browser_evaluate_blocked");
                }

                return $"Error: JavaScript expression contains blocked API '{blocked}'. " +
                       "Cookie access, storage access, and cross-origin communication APIs are not permitted.";
            }
        }

        // Audit log JS evaluation — log the full expression for forensic review
        if (auditLogger is not null)
        {
            _ = auditLogger.LogCommandAsync(
                                $"browser_evaluate: {expression}", ChannelName, userId: null,
                                allowed: true, success: true, ct: ct)
                            .LogExceptions(logger, "audit:browser_evaluate");
        }

        var session = sessions.GetOrCreate(sessionId);
        var page = await session.GetPageAsync(ct).ConfigureAwait(false);

        var result = await page.EvaluateAsync<JsonElement>(expression).ConfigureAwait(false);
        var json = result.ToString() ?? "undefined";

        // Cap output
        if (json.Length > MaxEvalOutputChars)
        {
            json = json[..MaxEvalOutputChars] + "\n... (truncated)";
        }

        return json;
    }

    private async Task<string> CloseAsync(string sessionId)
    {
        await sessions.CloseSessionAsync(sessionId).ConfigureAwait(false);
        return "Browser session closed.";
    }

    /// <summary>
    /// Injects data-pw-ref attributes onto interactive elements and returns an ARIA snapshot
    /// with [ref=eNN] annotations so the LLM can target elements by reference ID.
    /// </summary>
    private static async Task<string> CaptureAnnotatedSnapshotAsync(IPage page, string selector)
    {
        // Step 1: Inject ref annotations into interactive elements via JavaScript.
        // This assigns data-pw-ref="eN" to all interactive elements (links, buttons, inputs,
        // selects, textareas, and elements with click handlers or roles).
        await page.EvaluateAsync("""
                                 (() => {
                                     // Remove old refs
                                     document.querySelectorAll('[data-pw-ref]').forEach(el => el.removeAttribute('data-pw-ref'));
                                     // Tag interactive elements
                                     const interactiveSelectors = [
                                         'a[href]', 'button', 'input', 'select', 'textarea',
                                         '[role="button"]', '[role="link"]', '[role="checkbox"]', '[role="radio"]',
                                         '[role="tab"]', '[role="menuitem"]', '[role="option"]', '[role="switch"]',
                                         '[role="combobox"]', '[role="searchbox"]', '[role="textbox"]', '[role="slider"]',
                                         '[tabindex]', '[onclick]', '[contenteditable="true"]'
                                     ];
                                     const elements = document.querySelectorAll(interactiveSelectors.join(','));
                                     let counter = 1;
                                     elements.forEach(el => {
                                         if (!el.closest('[hidden]') && el.offsetParent !== null) {
                                             el.setAttribute('data-pw-ref', 'e' + counter);
                                             counter++;
                                         }
                                     });
                                 })()
                                 """).ConfigureAwait(false);

        // Step 2: Capture the standard ARIA snapshot.
        var locator = page.Locator(selector);
        var yaml = await locator.AriaSnapshotAsync(new LocatorAriaSnapshotOptions
        {
            Timeout = OperationTimeoutMs,
        }).ConfigureAwait(false);

        // Step 3: Augment the ARIA snapshot with ref annotations.
        // We query all ref-annotated elements and build a lookup of accessible name/role -> ref.
        var refMapJson = await page.EvaluateAsync<string>("""
                                                          (() => {
                                                              const refs = document.querySelectorAll('[data-pw-ref]');
                                                              const map = [];
                                                              refs.forEach(el => {
                                                                  const ref = el.getAttribute('data-pw-ref');
                                                                  const role = el.getAttribute('role') || el.tagName.toLowerCase();
                                                                  const name = el.getAttribute('aria-label')
                                                                      || el.textContent?.trim().substring(0, 60)
                                                                      || el.getAttribute('placeholder')
                                                                      || el.getAttribute('title')
                                                                      || '';
                                                                  map.push({ ref, role, name });
                                                              });
                                                              return JSON.stringify(map);
                                                          })()
                                                          """).ConfigureAwait(false);

        // Step 4: Build ref legend and prepend to snapshot.
        var sb = new StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(refMapJson);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var refId = item.GetProperty("ref").GetString();
                var role = item.GetProperty("role").GetString();
                var name = item.GetProperty("name").GetString();
                if (refId is not null)
                {
                    var displayName = string.IsNullOrWhiteSpace(name) ? role ?? "element" : $"{role}: \"{name}\"";
                    sb.AppendLine($"[{refId}] {displayName}");
                }
            }
        }
        catch
        {
            // If ref extraction fails, return the raw snapshot without annotations.
        }

        if (sb.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Accessibility Tree ---");
        }

        sb.Append(yaml);
        return sb.ToString();
    }

    /// <summary>Validates that a refId matches the expected format (e.g. "e5", "e123") to prevent CSS selector injection.</summary>
    private static string? ValidateRefId(string refId)
    {
        if (RefIdRegex().IsMatch(refId))
        {
            return null;
        }

        return $"Error: invalid refId format '{refId}'. Expected format: e1, e2, e3, ...";
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Playwright error during '{Action}' action")]
    private static partial void LogPlaywrightError(ILogger logger, Exception exception, string action);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Unexpected error during browser '{Action}' action")]
    private static partial void LogUnexpectedBrowserError(ILogger logger, Exception exception, string action);
    
    [GeneratedRegex(@"^e\d+$", RegexOptions.None, 200)]
    private static partial Regex RefIdRegex();
}