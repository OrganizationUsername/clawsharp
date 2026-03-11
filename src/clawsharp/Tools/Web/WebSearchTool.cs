using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clawsharp.Config;
using Clawsharp.Security;
using Clawsharp.Config.Features;

using Clawsharp.Tools;
namespace Clawsharp.Tools.Web;

public sealed partial class WebSearchTool : Tool
{
    private readonly IHttpClientFactory _httpFactory;

    private readonly string? _braveApiKey;

    private readonly string? _exaApiKey;

    private readonly string? _tavilyApiKey;

    private readonly string? _searxngBaseUrl;

    private readonly string? _jinaApiKey;

    private readonly string? _firecrawlApiKey;

    private readonly string? _firecrawlBaseUrl;

    private readonly string? _perplexityApiKey;

    private readonly string? _perplexityModel;

    private readonly string? _glmApiKey;

    private readonly string? _glmModel;

    private readonly SearchProvider _activeProvider;

    /// <summary>Cached GLM JWT token and its expiry time.</summary>
    private string? _glmCachedJwt;

    private long _glmJwtExpiryMs;

    private readonly object _glmJwtLock = new();

    private enum SearchProvider
    {
        Brave,

        Exa,

        Tavily,

        Searxng,

        Jina,

        Firecrawl,

        Perplexity,

        Glm,

        DuckDuckGo
    }

    private readonly AuditLogger? _auditLogger;

    public WebSearchTool(IHttpClientFactory httpFactory, ToolsConfig config, AuditLogger? auditLogger = null)
    {
        _httpFactory = httpFactory;
        _braveApiKey = config.Brave?.ApiKey;
        _exaApiKey = config.Exa?.ApiKey;
        _tavilyApiKey = config.Tavily?.ApiKey;
        _searxngBaseUrl = config.Searxng?.BaseUrl;
        _jinaApiKey = config.Jina?.ApiKey;
        _firecrawlApiKey = config.Firecrawl?.ApiKey;
        _firecrawlBaseUrl = config.Firecrawl?.BaseUrl;
        _perplexityApiKey = config.Perplexity?.ApiKey;
        _perplexityModel = config.Perplexity?.Model;
        _glmApiKey = config.Glm?.ApiKey;
        _glmModel = config.Glm?.Model;
        _auditLogger = auditLogger;

        _activeProvider = _braveApiKey is { Length: > 0 } ? SearchProvider.Brave
            : _exaApiKey is { Length: > 0 } ? SearchProvider.Exa
            : _tavilyApiKey is { Length: > 0 } ? SearchProvider.Tavily
            : _searxngBaseUrl is { Length: > 0 } ? SearchProvider.Searxng
            : _jinaApiKey is { Length: > 0 } ? SearchProvider.Jina
            : _firecrawlApiKey is { Length: > 0 } ? SearchProvider.Firecrawl
            : _perplexityApiKey is { Length: > 0 } ? SearchProvider.Perplexity
            : _glmApiKey is { Length: > 0 } ? SearchProvider.Glm
            : SearchProvider.DuckDuckGo;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "web_search";

    public override ToolSensitivity Sensitivity => ToolSensitivity.High;

    public override string Description =>
        $"Search the web using {_activeProvider} (priority: Brave > Exa > Tavily > SearXNG > Jina > Firecrawl > Perplexity > GLM > DuckDuckGo). Returns top results with titles and snippets.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "query": { "type": "string", "description": "Search query" },
                                                       "count": { "type": "integer", "description": "Number of results (default 5, max 10)" }
                                                     },
                                                     "required": ["query"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var count = arguments.TryGetProperty("count", out var c) && c.TryGetInt32(out var cv) ? Math.Min(cv, 10) : 5;

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: query is required.";
        }

        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogFileAccessAsync(query, $"web_search:{_activeProvider}", ChannelName, success: true, ct: ct);
        }

        try
        {
            return _activeProvider switch
            {
                SearchProvider.Brave => await BraveSearchAsync(query, count, ct),
                SearchProvider.Exa => await SearchExaAsync(query, count, ct),
                SearchProvider.Tavily => await SearchTavilyAsync(query, count, ct),
                SearchProvider.Searxng => await SearchSearxngAsync(query, count, ct),
                SearchProvider.Jina => await SearchJinaAsync(query, ct),
                SearchProvider.Firecrawl => await SearchFirecrawlAsync(query, count, ct),
                SearchProvider.Perplexity => await SearchPerplexityAsync(query, count, ct),
                SearchProvider.Glm => await SearchGlmAsync(query, ct),
                _ => await DdgSearchAsync(query, count, ct)
            };
        }
        catch (Exception ex)
        {
            return $"Search error ({_activeProvider}): {ex.Message}";
        }
    }

    // ──────────────────────────────────────────────
    // Brave Search (existing)
    // ──────────────────────────────────────────────

    private async Task<string> BraveSearchAsync(string query, int count, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://api.search.brave.com/res/v1/web/search?q={encoded}&count={count}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Accept", "application/json");
        req.Headers.Add("X-Subscription-Token", _braveApiKey);

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);
        var results = new List<string>();

        if (doc.RootElement.TryGetProperty("web", out var web) &&
            web.TryGetProperty("results", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var link = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                results.Add($"**{title}**\n{snippet}\n{link}");
                if (results.Count >= count)
                {
                    break;
                }
            }
        }

        return results.Count > 0 ? string.Join("\n\n", results) : "No results found.";
    }

    // ──────────────────────────────────────────────
    // Exa Search
    // ──────────────────────────────────────────────

    private async Task<string> SearchExaAsync(string query, int count, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new ExaSearchRequest(query, count, "auto"),
            WebSearchJsonContext.Default.ExaSearchRequest);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.exa.ai/search");
        req.Headers.Add("x-api-key", _exaApiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var parsed = JsonDocument.Parse(json);
        var sb = new StringBuilder();

        foreach (var r in parsed.RootElement.GetProperty("results").EnumerateArray())
        {
            var title = r.TryGetProperty("title", out var t) ? t.GetString() : "";
            var rUrl = r.TryGetProperty("url", out var u) ? u.GetString() : "";
            var text = r.TryGetProperty("text", out var tx) ? tx.GetString() : "";
            var snippet = text is { Length: > 150 } ? text[..150] + "..." : text;
            sb.AppendLine($"**{title}**\n{snippet}\n{rUrl}");
        }

        return sb.Length > 0 ? sb.ToString().Trim() : "No results found.";
    }

    // ──────────────────────────────────────────────
    // Tavily Search
    // ──────────────────────────────────────────────

    private async Task<string> SearchTavilyAsync(string query, int count, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new TavilySearchRequest(_tavilyApiKey!, query, count, "basic"),
            WebSearchJsonContext.Default.TavilySearchRequest);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var parsed = JsonDocument.Parse(json);
        var sb = new StringBuilder();

        foreach (var r in parsed.RootElement.GetProperty("results").EnumerateArray())
        {
            var title = r.TryGetProperty("title", out var t) ? t.GetString() : "";
            var rUrl = r.TryGetProperty("url", out var u) ? u.GetString() : "";
            var content = r.TryGetProperty("content", out var cv) ? cv.GetString() : "";
            var snippet = content is { Length: > 150 } ? content[..150] + "..." : content;
            sb.AppendLine($"**{title}**\n{snippet}\n{rUrl}");
        }

        return sb.Length > 0 ? sb.ToString().Trim() : "No results found.";
    }

    // ──────────────────────────────────────────────
    // SearXNG Search (user-controlled URL — SSRF guard required)
    // ──────────────────────────────────────────────

    private async Task<string> SearchSearxngAsync(string query, int count, CancellationToken ct)
    {
        var url = $"{_searxngBaseUrl!.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json&categories=general&pageno=1";

        var ssrfError = await Security.SsrfGuard.CheckAsync(new Uri(url), ct);
        if (ssrfError is not null)
        {
            return $"Error: {ssrfError}";
        }

        using var client = _httpFactory.CreateClient("tools");
        var resp = await client.GetStringAsync(url, ct);
        using var parsed = JsonDocument.Parse(resp);
        var sb = new StringBuilder();
        var i = 0;

        foreach (var r in parsed.RootElement.GetProperty("results").EnumerateArray())
        {
            if (i++ >= count)
            {
                break;
            }

            var title = r.TryGetProperty("title", out var t) ? t.GetString() : "";
            var rUrl = r.TryGetProperty("url", out var u) ? u.GetString() : "";
            var content = r.TryGetProperty("content", out var cv) ? cv.GetString() : "";
            sb.AppendLine($"**{title}**\n{content}\n{rUrl}");
        }

        return sb.Length > 0 ? sb.ToString().Trim() : "No results found.";
    }

    // ──────────────────────────────────────────────
    // Jina Search
    // ──────────────────────────────────────────────

    private async Task<string> SearchJinaAsync(string query, CancellationToken ct)
    {
        var url = $"https://s.jina.ai/?q={Uri.EscapeDataString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        if (_jinaApiKey is { Length: > 0 })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jinaApiKey);
        }

        req.Headers.Add("Accept", "text/plain");

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var text = await resp.Content.ReadAsStringAsync(ct);
        return text.Length > 50_000 ? text[..50_000] + "\n...[truncated]" : text;
    }

    // ──────────────────────────────────────────────
    // Firecrawl Search
    // ──────────────────────────────────────────────

    private async Task<string> SearchFirecrawlAsync(string query, int count, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new FirecrawlSearchRequest(query, count),
            WebSearchJsonContext.Default.FirecrawlSearchRequest);

        var baseUrl = (_firecrawlBaseUrl ?? "https://api.firecrawl.dev").TrimEnd('/');
        var requestUrl = $"{baseUrl}/v1/search";

        // HIGH-04: SSRF-check the Firecrawl base URL (user-configurable in config)
        var ssrfError = await Security.SsrfGuard.CheckAsync(new Uri(requestUrl), ct);
        if (ssrfError is not null)
        {
            return $"Error: {ssrfError}";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _firecrawlApiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);
        var sb = new StringBuilder();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var r in data.EnumerateArray())
            {
                var title = r.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var rUrl = r.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var description = r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var snippet = description.Length > 150 ? description[..150] + "..." : description;
                sb.AppendLine($"**{title}**\n{snippet}\n{rUrl}");
            }
        }

        return sb.Length > 0 ? sb.ToString().Trim() : "No results found.";
    }

    // ──────────────────────────────────────────────
    // Perplexity Sonar Search
    // ──────────────────────────────────────────────

    private async Task<string> SearchPerplexityAsync(string query, int count, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new PerplexitySearchRequest(
                _perplexityModel ?? "sonar-pro",
                [new PerplexityMessage("user", query)],
                1024),
            WebSearchJsonContext.Default.PerplexitySearchRequest);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _perplexityApiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content))
        {
            var text = content.GetString() ?? "No results found.";
            return text.Length > 50_000 ? text[..50_000] + "\n...[truncated]" : text;
        }

        return "No results found.";
    }

    // ──────────────────────────────────────────────
    // GLM/Zhipu Web Search (chat-completion based, JWT auth)
    // ──────────────────────────────────────────────

    private async Task<string> SearchGlmAsync(string query, CancellationToken ct)
    {
        var jwt = GetOrCreateGlmJwt();

        var body = JsonSerializer.Serialize(
            new GlmSearchRequest(
                _glmModel ?? "web-search-pro",
                [new GlmMessage("user", query)]),
            WebSearchJsonContext.Default.GlmSearchRequest);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://open.bigmodel.cn/api/paas/v4/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content))
        {
            var text = content.GetString() ?? "No results found.";
            return text.Length > 50_000 ? text[..50_000] + "\n...[truncated]" : text;
        }

        return "No results found.";
    }

    /// <summary>
    /// Returns a cached JWT for the GLM API, regenerating when expired.
    /// JWT is cached for 3 minutes; the token itself is valid for 3.5 minutes.
    /// </summary>
    private string GetOrCreateGlmJwt()
    {
        lock (_glmJwtLock)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_glmCachedJwt is not null && nowMs < _glmJwtExpiryMs)
            {
                return _glmCachedJwt;
            }

            var apiKey = _glmApiKey ?? throw new InvalidOperationException("GLM API key is required.");
            var dotIndex = apiKey.IndexOf('.');
            if (dotIndex < 1 || dotIndex >= apiKey.Length - 1)
            {
                throw new InvalidOperationException("GLM API key must be in the format '{id}.{secret}'.");
            }

            var id = apiKey[..dotIndex];
            var secret = apiKey[(dotIndex + 1)..];

            var expMs = nowMs + (long)(3.5 * 60 * 1000); // 3.5 minutes

            var headerJson = """{"alg":"HS256","typ":"JWT","sign_type":"SIGN"}""";
            var payloadJson = $$"""{"api_key":"{{id}}","exp":{{expMs}},"timestamp":{{nowMs}}}""";

            var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            var signingInput = $"{headerB64}.{payloadB64}";
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var signatureBytes = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingInput));
            var signatureB64 = Base64UrlEncode(signatureBytes);

            var jwt = $"{headerB64}.{payloadB64}.{signatureB64}";

            // Cache for 3 minutes (regenerate 30s before actual expiry)
            _glmCachedJwt = jwt;
            _glmJwtExpiryMs = nowMs + (3 * 60 * 1000);

            return jwt;
        }
    }

    /// <summary>Base64url-encode without padding (RFC 7515).</summary>
    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
                      .Replace('+', '-')
                      .Replace('/', '_')
                      .TrimEnd('=');
    }

    // ──────────────────────────────────────────────
    // DuckDuckGo Lite HTML scrape (existing, always available)
    // ──────────────────────────────────────────────

    private async Task<string> DdgSearchAsync(string query, int count, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://lite.duckduckgo.com/lite/?q={encoded}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; clawsharp/1.0)");
        using var client = _httpFactory.CreateClient("tools");
        using var resp = await client.SendAsync(req, ct);
        var html = await resp.Content.ReadAsStringAsync(ct);

        // Extract result links and snippets with compiled regex
        var results = new List<string>();
        var links = ResultLinkRegex().Matches(html);
        var snippets = ResultSnippetRegex().Matches(html);

        for (var i = 0; i < Math.Min(links.Count, count); i++)
        {
            var link = WebUtility.HtmlDecode(links[i].Groups[1].Value);
            var title = WebUtility.HtmlDecode(links[i].Groups[2].Value).Trim();
            var snippet = i < snippets.Count
                ? HtmlTagRegex().Replace(WebUtility.HtmlDecode(snippets[i].Groups[1].Value), "").Trim()
                : "";
            results.Add($"**{title}**\n{snippet}\n{link}");
        }

        return results.Count > 0 ? string.Join("\n\n", results) : "No results found.";
    }

    // ──────────────────────────────────────────────
    // Compiled regex (DuckDuckGo HTML parsing)
    // ──────────────────────────────────────────────

    [GeneratedRegex(@"<a[^>]+class=""result-link""[^>]*href=""([^""]+)""[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex ResultLinkRegex();

    [GeneratedRegex(@"<td[^>]+class=""result-snippet""[^>]*>([\s\S]+?)</td>", RegexOptions.IgnoreCase)]
    private static partial Regex ResultSnippetRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}

// ──────────────────────────────────────────────
// AOT-safe JSON DTOs + source-generated context
// ──────────────────────────────────────────────

internal sealed record ExaSearchRequest(string query, int numResults, string type);

internal sealed record TavilySearchRequest(string api_key, string query, int max_results, string search_depth);

internal sealed record FirecrawlSearchRequest(string query, int limit);

internal sealed record PerplexityMessage(string role, string content);

internal sealed record PerplexitySearchRequest(
    string model,
    IReadOnlyList<PerplexityMessage> messages,
    int max_tokens);

internal sealed record GlmMessage(string role, string content);

internal sealed record GlmSearchRequest(
    string model,
    IReadOnlyList<GlmMessage> messages);

[JsonSerializable(typeof(ExaSearchRequest))]
[JsonSerializable(typeof(TavilySearchRequest))]
[JsonSerializable(typeof(FirecrawlSearchRequest))]
[JsonSerializable(typeof(PerplexityMessage))]
[JsonSerializable(typeof(PerplexitySearchRequest))]
[JsonSerializable(typeof(IReadOnlyList<PerplexityMessage>))]
[JsonSerializable(typeof(GlmMessage))]
[JsonSerializable(typeof(GlmSearchRequest))]
[JsonSerializable(typeof(IReadOnlyList<GlmMessage>))]
internal partial class WebSearchJsonContext : JsonSerializerContext;