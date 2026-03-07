using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Providers;

/// <summary>
/// Shared HTTP helper for LLM providers. Extracts the common
/// serialize-send-deserialize pattern used by Anthropic, OpenAI, and Gemini.
/// </summary>
internal static partial class ProviderHttpHelper
{
    /// <summary>
    /// Serializes an <see cref="IRequest{TResponse}"/>, sends it via HTTP POST,
    /// and deserializes the response body. Error bodies are capped at 4 KB and sanitized.
    /// <para>
    /// Uses <see cref="JsonSerializer.SerializeToUtf8Bytes(object?, JsonTypeInfo)"/> to avoid
    /// the intermediate UTF-16 string allocation that <see cref="StringContent"/> would require.
    /// </para>
    /// </summary>
    /// <param name="httpFactory">Factory for named HTTP clients (uses the "llm" client).</param>
    /// <param name="request">Self-describing request carrying URL and type metadata.</param>
    /// <param name="configureRequest">Callback to set provider-specific headers on the <see cref="HttpRequestMessage"/>.</param>
    /// <param name="providerName">Human-readable provider name for error messages (e.g. "Anthropic API").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <typeparam name="TResponse">Deserialized response type.</typeparam>
    /// <returns>The deserialized response, or <c>null</c> if the body was empty.</returns>
    internal static async Task<TResponse?> ExecuteAsync<TResponse>(
        IHttpClientFactory httpFactory,
        IRequest<TResponse> request,
        Action<HttpRequestMessage> configureRequest,
        string providerName,
        CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, request.RequestTypeInfo);

        using var http = httpFactory.CreateClient("llm");
        using var req = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new ReadOnlyMemoryContent(bytes)
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        configureRequest(req);

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            ThrowForErrorResponse(resp, await ReadCappedErrorBodyAsync(resp, ct).ConfigureAwait(false), providerName);
        }

        // Detect HTML responses from misconfigured proxies (can happen even on 200)
        ThrowIfHtmlContentType(resp, providerName);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, request.ResponseTypeInfo, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares a streaming HTTP POST: serializes the body to UTF-8 bytes, sends with
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/>, validates the response status,
    /// and returns the response stream for SSE parsing.
    /// <para>
    /// The caller owns disposal of the returned <see cref="HttpClient"/> and <see cref="HttpResponseMessage"/>.
    /// </para>
    /// </summary>
    internal static async Task<(HttpClient Http, HttpResponseMessage Response, Stream Body)> SendStreamingAsync(
        IHttpClientFactory httpFactory,
        string url,
        ReadOnlyMemory<byte> jsonBody,
        Action<HttpRequestMessage> configureRequest,
        string providerName,
        CancellationToken ct)
    {
        var http = httpFactory.CreateClient("llm");
        HttpResponseMessage? resp = null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ReadOnlyMemoryContent(jsonBody)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            configureRequest(req);

            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                ThrowForErrorResponse(resp, await ReadCappedErrorBodyAsync(resp, ct).ConfigureAwait(false), providerName);
            }

            // Detect HTML responses from misconfigured proxies (can happen even on 200)
            ThrowIfHtmlContentType(resp, providerName);

            var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return (http, resp, body);
        }
        catch
        {
            resp?.Dispose();
            http.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads up to 4 KB of the error response body and converts it to a UTF-8 string.
    /// </summary>
    private static async Task<string> ReadCappedErrorBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var errBytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var limitedBytes = errBytes.Length > 4096 ? errBytes.AsSpan(0, 4096) : errBytes.AsSpan();
        return Encoding.UTF8.GetString(limitedBytes);
    }

    /// <summary>
    /// Throws an <see cref="HttpRequestException"/> with a sanitized error body.
    /// Marked as <see langword="DoesNotReturn"/> so callers don't need extra null checks.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowForErrorResponse(HttpResponseMessage resp, string errorBody, string providerName)
    {
        if (IsHtmlResponse(resp, errorBody))
        {
            throw new HttpRequestException(
                $"{providerName} returned HTML instead of JSON (HTTP {(int)resp.StatusCode}). " +
                "Check your API base URL and proxy configuration.",
                null,
                resp.StatusCode);
        }

        throw new HttpRequestException(
            $"{providerName} error {resp.StatusCode}: {SanitizeErrorBody(errorBody)}",
            null,
            resp.StatusCode);
    }

    /// <summary>
    /// Throws if the response Content-Type is HTML — catches misconfigured proxies returning
    /// HTML error pages on 2xx status codes before the JSON deserializer crashes.
    /// </summary>
    private static void ThrowIfHtmlContentType(HttpResponseMessage resp, string providerName)
    {
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        if (contentType is "text/html" or "application/xhtml+xml")
        {
            throw new HttpRequestException(
                $"{providerName} returned HTML instead of JSON. " +
                "Check your API base URL and proxy configuration.");
        }
    }

    /// <summary>
    /// Detects HTML responses by Content-Type header or body prefix.
    /// </summary>
    private static bool IsHtmlResponse(HttpResponseMessage resp, string body)
    {
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        if (contentType is "text/html" or "application/xhtml+xml")
            return true;

        // Also check body prefix for cases where Content-Type is wrong/missing
        var trimmed = body.AsSpan().TrimStart();
        return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    // ── Error Body Sanitization ──────────────────────────────────────────────

    /// <summary>
    /// Sanitizes a raw API error body by truncating to 500 characters and stripping
    /// patterns that may contain secrets (API keys, bearer tokens).
    /// </summary>
    internal static string SanitizeErrorBody(string raw)
    {
        // First, strip any patterns that look like API keys or bearer tokens.
        var cleaned = SecretPatternRegex().Replace(raw, "[REDACTED]");

        // Truncate to a safe display length.
        return cleaned.Length > 500 ? string.Concat(cleaned.AsSpan(0, 500), "... (truncated)") : cleaned;
    }

    /// <summary>
    /// Matches common secret patterns in error bodies:
    /// <list type="bullet">
    /// <item>OpenAI-style keys: <c>sk-[A-Za-z0-9]{20,}</c></item>
    /// <item>Anthropic-style keys: <c>sk-ant-[A-Za-z0-9\-]{20,}</c></item>
    /// <item>Bearer tokens in echoed text: <c>Bearer [^\s"]{20,}</c></item>
    /// <item>Generic long hex strings (40+ chars, likely keys): <c>[0-9a-fA-F]{40,}</c></item>
    /// </list>
    /// </summary>
    [GeneratedRegex(
        @"sk-ant-[A-Za-z0-9\-]{20,}|sk-[A-Za-z0-9]{20,}|key-[A-Za-z0-9]{20,}|Bearer\s+[^\s""]{20,}|[0-9a-fA-F]{40,}")]
    private static partial Regex SecretPatternRegex();
}