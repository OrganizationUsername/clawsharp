using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawsharp.Security;

namespace Clawsharp.Tools.Web;

public sealed partial class WebFetchTool : Tool
{
    private readonly IHttpClientFactory _httpFactory;

    private readonly AuditLogger? _auditLogger;

    private readonly IReadOnlyList<string>? _allowedDomains;

    public WebFetchTool(IHttpClientFactory httpFactory, AuditLogger? auditLogger = null,
                        IReadOnlyList<string>? allowedDomains = null)
    {
        _httpFactory = httpFactory;
        _auditLogger = auditLogger;
        _allowedDomains = allowedDomains;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "web_fetch";

    public override ToolSensitivity Sensitivity => ToolSensitivity.High;

    public override string Description => "Fetch the content of a URL. Returns HTML stripped to plain text. Supports GET and POST.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "url": { "type": "string", "description": "URL to fetch" },
                                                       "method": { "type": "string", "enum": ["GET", "POST"], "description": "HTTP method (default GET)" },
                                                       "body": { "type": "string", "description": "Request body for POST" },
                                                       "max_chars": { "type": "integer", "description": "Maximum characters to return (default 100000)" }
                                                     },
                                                     "required": ["url"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var url = arguments.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        var method = arguments.TryGetProperty("method", out var m) ? m.GetString() ?? "GET" : "GET";
        var body = arguments.TryGetProperty("body", out var b) ? b.GetString() : null;
        var maxChars = arguments.TryGetProperty("max_chars", out var mc) && mc.TryGetInt32(out var mcVal) ? mcVal : 100_000;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"Error: invalid URL: {url}";
        }

        // Centralized SSRF protection (scheme, hostname, DNS, IP checks)
        var ssrfError = await SsrfGuard.CheckAsync(uri, ct).ConfigureAwait(false);
        if (ssrfError is not null)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogPolicyViolationAsync(
                    $"SSRF blocked: {url}", ChannelName, ssrfBlocked: true, ct: ct);
            }

            return ssrfError;
        }

        // Domain allowlist check (when configured)
        var domainError = SsrfGuard.CheckDomainAllowlist(uri, _allowedDomains);
        if (domainError is not null)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogPolicyViolationAsync(
                    $"Domain blocked: {url}", ChannelName, ct: ct);
            }

            return domainError;
        }

        try
        {
            using var client = _httpFactory.CreateClient("tools");
            using var resp = method.Equals("POST", StringComparison.OrdinalIgnoreCase) && body is not null
                ? await client.PostAsync(uri, new StringContent(body, Encoding.UTF8, "application/json"), ct)
                : await client.GetAsync(uri, ct);

            var text = await resp.Content.ReadAsStringAsync(ct);
            text = StripHtml(text);
            if (text.Length > maxChars)
            {
                text = text[..maxChars] + "\n... (truncated)";
            }

            return $"[{(int)resp.StatusCode}] {text}";
        }
        catch (Exception ex)
        {
            return $"Error fetching {url}: {ex.Message}";
        }
    }

    private static string StripHtml(string html)
    {
        html = ScriptTagRegex().Replace(html, " ");
        html = StyleTagRegex().Replace(html, " ");
        html = HtmlTagRegex().Replace(html, " ");
        html = MultiWhitespaceRegex().Replace(html, " ");
        return WebUtility.HtmlDecode(html).Trim();
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiWhitespaceRegex();
}