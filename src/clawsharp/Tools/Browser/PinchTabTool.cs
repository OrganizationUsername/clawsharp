using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Features;

using Clawsharp.Tools;
namespace Clawsharp.Tools.Browser;

/// <summary>
/// Browser automation tool that talks to a running PinchTab HTTP server (CDP-based Chrome controller).
/// Provides 13 actions: navigate, snapshot, text, click, fill, type, press, select, scroll,
/// screenshot, evaluate, tabs, close.
///
/// PinchTab uses stable element refs (e1, e2, ...) from the CDP accessibility tree and is
/// 5-13x more token-efficient than screenshots/DOM.
///
/// Requires a running PinchTab server (default: http://localhost:9867). Start with: pinchtab
/// </summary>
public sealed partial class PinchTabTool : Tool
{
    private readonly PinchTabSessionManager _sessions;

    private readonly PinchTabConfig _config;

    private readonly IHttpClientFactory _httpFactory;

    private readonly AuditLogger? _auditLogger;

    private readonly ILogger<PinchTabTool> _logger;

    /// <summary>Session ID read from the per-async-flow AsyncLocal in ToolRegistry.</summary>
    public string? SessionId => ToolRegistry.CurrentSessionId;

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public PinchTabTool(
        PinchTabSessionManager sessions,
        IOptions<AppConfig> configOptions,
        IHttpClientFactory httpFactory,
        AuditLogger? auditLogger,
        ILogger<PinchTabTool> logger)
    {
        _sessions = sessions;
        _config = configOptions.Value.Tools.PinchTab;
        _httpFactory = httpFactory;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public override string Name => "pinchtab_browser";

    public override ToolSensitivity Sensitivity => ToolSensitivity.High;

    public override string Description =>
        "Browser automation via a running PinchTab server (CDP-based Chrome controller). " +
        "Navigate pages, take accessibility snapshots with element refs, click/fill/type/press elements, " +
        "take screenshots, evaluate JavaScript, and manage tabs. " +
        "Requires a running PinchTab server (default: http://localhost:9867). Start with: pinchtab";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "action": {
                                                         "type": "string",
                                                         "enum": ["navigate","snapshot","text","click","fill","type","press","select","scroll","screenshot","evaluate","tabs","close"],
                                                         "description": "The browser action to perform"
                                                       },
                                                       "url": {"type":"string","description":"URL to navigate to (for 'navigate')"},
                                                       "ref": {"type":"string","description":"Element ref ID from snapshot, e.g. 'e5' (for click/fill/type/press/select/scroll)"},
                                                       "text": {"type":"string","description":"Text to type/fill (for 'type'/'fill')"},
                                                       "value": {"type":"string","description":"Value to set (for 'select', or alternative to 'text' for 'fill')"},
                                                       "key": {"type":"string","description":"Key to press, e.g. 'Enter', 'Tab' (for 'press')"},
                                                       "expression": {"type":"string","description":"JavaScript expression (for 'evaluate', requires evaluateEnabled config)"},
                                                       "filter": {"type":"string","enum":["interactive","all"],"description":"Snapshot filter (default: 'interactive')"},
                                                       "maxTokens": {"type":"integer","description":"Max tokens for snapshot output (default: 4000)"}
                                                     },
                                                     "required": ["action"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return "Error: pinchtab_browser tool is disabled in configuration (tools.pinchTab.enabled = false).";
        }

        var action = arguments.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        var sessionId = SessionId ?? "default";

        try
        {
            return action switch
            {
                "navigate" => await NavigateAsync(arguments, sessionId, ct),
                "snapshot" => await SnapshotAsync(arguments, sessionId, ct),
                "text" => await TextAsync(sessionId, ct),
                "click" => await ActionAsync("click", arguments, sessionId, ct),
                "fill" => await ActionAsync("fill", arguments, sessionId, ct),
                "type" => await ActionAsync("type", arguments, sessionId, ct),
                "press" => await ActionAsync("press", arguments, sessionId, ct),
                "select" => await ActionAsync("select", arguments, sessionId, ct),
                "scroll" => await ActionAsync("scroll", arguments, sessionId, ct),
                "screenshot" => await ScreenshotAsync(sessionId, ct),
                "evaluate" => await EvaluateAsync(arguments, sessionId, ct),
                "tabs" => await TabsAsync(ct),
                "close" => await CloseAsync(sessionId, ct),
                _ => $"Error: unknown pinchtab action '{action}'. " +
                     "Valid actions: navigate, snapshot, text, click, fill, type, press, select, scroll, screenshot, evaluate, tabs, close.",
            };
        }
        catch (HttpRequestException ex)
        {
            LogPinchTabConnectionError(_logger, ex, action);
            return $"Error: cannot connect to PinchTab server at {_config.BaseUrl}. Start it with: pinchtab";
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return $"Error: PinchTab request timed out: {ex.Message}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpectedPinchTabError(_logger, ex, action);
            return $"Error: {ex.Message}";
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, _config.BaseUrl.TrimEnd('/') + path);
        if (!string.IsNullOrEmpty(_config.Token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.Token);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("pinchtab");
        return await client.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<string?> CheckResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return $"Error: PinchTab returned {(int)response.StatusCode}: {Truncate(body, 500)}";
    }

    private string? RequireTabId(string sessionId)
    {
        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab — use 'navigate' first.";
        }

        return null;
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
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogPolicyViolationAsync(
                                    $"SSRF blocked PinchTab navigation: {url}", ChannelName, ssrfBlocked: true, ct: ct)
                                .LogExceptions(_logger, "audit:pinchtab_ssrf");
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
        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogFileAccessAsync(url, "pinchtab_navigate", ChannelName, success: true, ct: ct)
                            .LogExceptions(_logger, "audit:pinchtab_navigate");
        }

        var existingTabId = _sessions.GetTabId(sessionId);
        var body = new PtNavigateRequest(url, existingTabId);

        var request = CreateRequest(HttpMethod.Post, "/navigate");
        request.Content = JsonContent.Create(body, PinchTabJsonContext.Default.PtNavigateRequest);

        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var nav = JsonSerializer.Deserialize(json, PinchTabJsonContext.Default.PtNavigateResponse);
        if (nav is null)
        {
            return "Error: failed to parse PinchTab navigate response.";
        }

        _sessions.SetTabId(sessionId, nav.TabId);
        return $"Navigated to {nav.Url}. Tab: {nav.TabId}. Title: {nav.Title}";
    }

    private async Task<string> SnapshotAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab — use 'navigate' first.";
        }

        var filter = args.TryGetProperty("filter", out var f) ? f.GetString() ?? "interactive" : "interactive";
        var maxTokens = args.TryGetProperty("maxTokens", out var mt) && mt.TryGetInt32(out var mtv) ? mtv : 4000;

        var request = CreateRequest(HttpMethod.Get,
            $"/snapshot?tabId={Uri.EscapeDataString(tabId)}&filter={Uri.EscapeDataString(filter)}&format=text&maxTokens={maxTokens}");
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Snapshot returned empty content. The page may not have loaded yet.";
        }

        return text + "\n\nUse ref IDs (e.g. 'e5') with click/fill/type/press actions.";
    }

    private async Task<string> TextAsync(string sessionId, CancellationToken ct)
    {
        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab — use 'navigate' first.";
        }

        var request = CreateRequest(HttpMethod.Get,
            $"/text?tabId={Uri.EscapeDataString(tabId)}");
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Length > 16_000)
        {
            text = text[..16_000] + "\n... (truncated)";
        }

        return text;
    }

    private async Task<string> ActionAsync(string kind, JsonElement args, string sessionId, CancellationToken ct)
    {
        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab — use 'navigate' first.";
        }

        var refId = args.TryGetProperty("ref", out var r) ? r.GetString() : null;
        if (refId is not null && !Regex.IsMatch(refId, @"^e\d+$"))
        {
            return $"Error: invalid ref format '{refId}'. Expected format: e1, e2, e3, ...";
        }

        var text = args.TryGetProperty("text", out var t) ? t.GetString() : null;
        var value = args.TryGetProperty("value", out var v) ? v.GetString() : null;
        var key = args.TryGetProperty("key", out var k) ? k.GetString() : null;

        var body = new PtActionRequest(kind, tabId, refId, text, value, key);
        var request = CreateRequest(HttpMethod.Post, "/action");
        request.Content = JsonContent.Create(body, PinchTabJsonContext.Default.PtActionRequest);

        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        return $"Action '{kind}' completed.{(refId is not null ? $" Target: [{refId}]." : "")}";
    }

    private async Task<string> ScreenshotAsync(string sessionId, CancellationToken ct)
    {
        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab — use 'navigate' first.";
        }

        var request = CreateRequest(HttpMethod.Get,
            $"/screenshot?tabId={Uri.EscapeDataString(tabId)}");
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-screenshots");
        Directory.CreateDirectory(tempDir);
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var filename = $"pinchtab-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{shortId}.png";
        var path = Path.Combine(tempDir, filename);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

        return $"Screenshot saved to {path} ({bytes.Length:N0} bytes).";
    }

    private async Task<string> EvaluateAsync(JsonElement args, string sessionId, CancellationToken ct)
    {
        if (!_config.EvaluateEnabled)
        {
            return "Error: JavaScript evaluation is disabled in configuration (tools.pinchTab.evaluateEnabled = false). " +
                   "Enable it only if you trust the LLM's output.";
        }

        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab — use 'navigate' first.";
        }

        var expression = args.TryGetProperty("expression", out var e) ? e.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "Error: 'expression' is required for the 'evaluate' action.";
        }

        // Audit log JS evaluation
        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogCommandAsync(
                                $"pinchtab_evaluate: {Truncate(expression, 200)}", ChannelName, userId: null,
                                allowed: true, success: true, ct: ct)
                            .LogExceptions(_logger, "audit:pinchtab_evaluate");
        }

        var body = new PtEvaluateRequest(tabId, expression);
        var request = CreateRequest(HttpMethod.Post, "/evaluate");
        request.Content = JsonContent.Create(body, PinchTabJsonContext.Default.PtEvaluateRequest);

        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var result = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (result.Length > 8_000)
        {
            result = result[..8_000] + "\n... (truncated)";
        }

        return result;
    }

    private async Task<string> TabsAsync(CancellationToken ct)
    {
        var request = CreateRequest(HttpMethod.Get, "/tabs");
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var tabs = JsonSerializer.Deserialize(json, PinchTabJsonContext.Default.PtTabsResponse);
        if (tabs is null || tabs.Tabs.Length == 0)
        {
            return "No open tabs.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Open tabs ({tabs.Tabs.Length}):");
        foreach (var tab in tabs.Tabs)
        {
            sb.AppendLine($"  [{tab.Id}] {tab.Title} — {tab.Url}");
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string> CloseAsync(string sessionId, CancellationToken ct)
    {
        var tabId = _sessions.GetTabId(sessionId);
        if (tabId is null)
        {
            return "Error: no active tab to close.";
        }

        var body = new PtTabCloseRequest("close", tabId);
        var request = CreateRequest(HttpMethod.Post, "/tab");
        request.Content = JsonContent.Create(body, PinchTabJsonContext.Default.PtTabCloseRequest);

        var response = await SendAsync(request, ct).ConfigureAwait(false);
        var error = await CheckResponseAsync(response).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        _sessions.RemoveTabId(sessionId);
        return $"Closed tab {tabId}.";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Cannot connect to PinchTab server for '{Action}' action")]
    private static partial void LogPinchTabConnectionError(ILogger logger, Exception exception, string action);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Unexpected error during pinchtab '{Action}' action")]
    private static partial void LogUnexpectedPinchTabError(ILogger logger, Exception exception, string action);
}