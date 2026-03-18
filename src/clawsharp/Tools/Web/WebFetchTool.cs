using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawsharp.Security;

namespace Clawsharp.Tools.Web;

public sealed partial class WebFetchTool(
    IHttpClientFactory httpFactory,
    AuditLogger? auditLogger = null,
    IReadOnlyList<string>? allowedDomains = null)
    : Tool
{
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
            if (auditLogger is not null)
            {
                _ = auditLogger.LogPolicyViolationAsync(
                    $"SSRF blocked: {url}", ChannelName, ssrfBlocked: true, ct: ct);
            }

            return ssrfError;
        }

        // Domain allowlist check (when configured)
        var domainError = SsrfGuard.CheckDomainAllowlist(uri, allowedDomains);
        if (domainError is not null)
        {
            if (auditLogger is not null)
            {
                _ = auditLogger.LogPolicyViolationAsync(
                    $"Domain blocked: {url}", ChannelName, ct: ct);
            }

            return domainError;
        }

        try
        {
            using var client = httpFactory.CreateClient("tools");

            HttpResponseMessage resp;
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && body is not null)
            {
                resp = await client.PostAsync(uri, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            }
            else
            {
                resp = await client.GetAsync(uri, ct);
            }

            using (resp)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                // Cap raw HTML before regex processing to prevent excessive scan time on huge responses.
                if (text.Length > maxChars * 2)
                {
                    text = text[..(maxChars * 2)];
                }

                text = StripHtml(text);
                if (text.Length > maxChars)
                {
                    text = text[..maxChars] + "\n... (truncated)";
                }

                return $"[{(int)resp.StatusCode}] {text}";
            }
        }
        catch (Exception)
        {
            return "Error: network request failed.";
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

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase, 200)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase, 200)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, 200)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, 200)]
    private static partial Regex MultiWhitespaceRegex();
}