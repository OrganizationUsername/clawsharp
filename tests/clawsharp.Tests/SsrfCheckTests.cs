using System.Reflection;
using System.Text.Json;
using Clawsharp.Tools;
using Clawsharp.Tools.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Clawsharp.Tests;

public sealed class SsrfCheckTests
{
    // ── URL validation and scheme blocking (via ExecuteAsync) ─────────

    private static IHttpClientFactory CreateHttpFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("tools", c => c.Timeout = TimeSpan.FromSeconds(10));
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private static async Task<string> FetchUrl(string url)
    {
        var tool = new WebFetchTool(CreateHttpFactory());
        var json = JsonSerializer.Serialize(new { url });
        using var doc = JsonDocument.Parse(json);
        return await tool.ExecuteAsync(doc.RootElement);
    }

    [Test]
    public async Task FetchUrl_FileScheme_ReturnsBlocked()
    {
        var result = await FetchUrl("file:///etc/passwd");

        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task FetchUrl_FtpScheme_ReturnsBlocked()
    {
        var result = await FetchUrl("ftp://example.com/file");

        result.ShouldContain("[SSRF] Blocked");
    }

    [Test]
    public async Task FetchUrl_InvalidUrl_ReturnsError()
    {
        var result = await FetchUrl("not-a-url");

        result.ToLowerInvariant().ShouldContain("error");
    }

    [Test]
    public async Task FetchUrl_HttpWithNoHost_ReturnsError()
    {
        var result = await FetchUrl("http://");

        (result.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
         result.Contains("SSRF", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue($"Expected error or SSRF message, got: {result}");
    }

    // ── StripHtml tests (via reflection) ─────────────────────────────

    private static string InvokeStripHtml(string html)
    {
        var method = typeof(WebFetchTool).GetMethod("StripHtml",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [html])!;
    }

    [Test]
    public void StripHtml_ScriptTags_RemovesScripts()
    {
        var html = "<p>Hello</p><script>alert('xss')</script><p>World</p>";

        var result = InvokeStripHtml(html);

        result.ShouldNotContain("alert");
        result.ShouldContain("Hello");
        result.ShouldContain("World");
    }

    [Test]
    public void StripHtml_StyleTags_RemovesStyles()
    {
        var html = "<style>body { color: red; }</style><p>Content</p>";

        var result = InvokeStripHtml(html);

        result.ShouldNotContain("color");
        result.ShouldContain("Content");
    }

    [Test]
    public void StripHtml_HtmlTags_StripsAllTags()
    {
        var html = "<div><b>Bold</b> and <i>italic</i></div>";

        var result = InvokeStripHtml(html);

        result.ShouldNotContain("<");
        result.ShouldContain("Bold");
        result.ShouldContain("italic");
    }

    [Test]
    public void StripHtml_HtmlEntities_DecodesEntities()
    {
        var html = "<p>Fish &amp; Chips &lt;3</p>";

        var result = InvokeStripHtml(html);

        result.ShouldContain("Fish & Chips");
        result.ShouldContain("<3");
    }

    [Test]
    public void StripHtml_MultipleSpaces_CollapsesWhitespace()
    {
        var html = "<p>A</p>   <p>B</p>     <p>C</p>";

        var result = InvokeStripHtml(html);

        // Should not have runs of more than 1 space
        result.ShouldNotContain("  ");
    }
}