namespace Clawsharp.Config.Agent;

/// <summary>HTTP request configuration for outbound provider/tool calls.</summary>
public sealed class HttpRequestConfig
{
    /// <summary>
    /// HTTP or SOCKS5 proxy URL for LLM provider requests.
    /// Falls back to <c>HTTPS_PROXY</c>, <c>HTTP_PROXY</c>, or <c>ALL_PROXY</c> environment variables when null.
    /// Example: <c>http://proxy.example.com:8080</c> or <c>socks5://127.0.0.1:1080</c>.
    /// </summary>
    public string? Proxy { get; init; }
}