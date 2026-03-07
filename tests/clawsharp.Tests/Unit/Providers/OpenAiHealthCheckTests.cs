using System.Net;
using Clawsharp.Providers;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Unit tests for <see cref="OpenAiProvider.CheckHealthAsync"/>.
/// Uses a fake HTTP message handler to simulate API responses without network calls.
/// </summary>
[TestFixture]
public sealed class OpenAiHealthCheckTests
{
    // -- 1. Returns healthy when HTTP 200 --

    [Test]
    public async Task CheckHealthAsync_Http200_ReturnsHealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeTrue();
        result.Message.ShouldBe("HTTP 200");
        result.ResponseTime.ShouldNotBeNull();
        result.ResponseTime!.Value.TotalMilliseconds.ShouldBeGreaterThanOrEqualTo(0);
    }

    // -- 2. Returns unhealthy when HTTP 500 --

    [Test]
    public async Task CheckHealthAsync_Http500_ReturnsUnhealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.InternalServerError, """{"error":"internal"}""");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("500");
        result.ResponseTime.ShouldNotBeNull();
    }

    // -- 3. Returns unhealthy when HTTP 401 --

    [Test]
    public async Task CheckHealthAsync_Http401_ReturnsUnhealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.Unauthorized, """{"error":"unauthorized"}""");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("401");
    }

    // -- 4. Returns unhealthy when HTTP 429 --

    [Test]
    public async Task CheckHealthAsync_Http429_ReturnsUnhealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.TooManyRequests, """{"error":"rate_limited"}""");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("429");
    }

    // -- 5. Returns unhealthy when connection refused (HttpRequestException) --

    [Test]
    public async Task CheckHealthAsync_ConnectionRefused_ReturnsUnhealthy()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused"));
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("Connection refused");
        result.ResponseTime.ShouldNotBeNull();
    }

    // -- 6. Returns unhealthy when timeout (TaskCanceledException) --

    [Test]
    public async Task CheckHealthAsync_Timeout_ReturnsUnhealthy()
    {
        var handler = new ThrowingHttpHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.ResponseTime.ShouldNotBeNull();
    }

    // -- 7. Response time is populated on success --

    [Test]
    public async Task CheckHealthAsync_Success_ResponseTimePopulated()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.ResponseTime.ShouldNotBeNull();
        result.ResponseTime!.Value.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // -- 8. Response time is populated on failure --

    [Test]
    public async Task CheckHealthAsync_Failure_ResponseTimePopulated()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.ServiceUnavailable, "");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.ResponseTime.ShouldNotBeNull();
    }

    // -- 9. Correct URL is called --

    [Test]
    public async Task CheckHealthAsync_CallsModelsEndpoint()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var provider = CreateProvider(handler, "https://api.example.com/v1");

        await provider.CheckHealthAsync();

        handler.LastRequestUri.ShouldNotBeNull();
        handler.LastRequestUri!.ToString().ShouldBe("https://api.example.com/v1/models");
    }

    // -- 10. Authorization header is set --

    [Test]
    public async Task CheckHealthAsync_SetsAuthorizationHeader()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var provider = CreateProvider(handler, apiKey: "sk-test-key-123");

        await provider.CheckHealthAsync();

        handler.LastAuthorizationHeader.ShouldNotBeNull();
        handler.LastAuthorizationHeader.ShouldBe("Bearer sk-test-key-123");
    }

    // -- 11. Custom auth header is used when specified --

    [Test]
    public async Task CheckHealthAsync_CustomAuthHeader_UsesCustomHeader()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenAiProvider(factory, "https://api.example.com/v1", "my-api-key", "azure-openai", "api-key");

        await provider.CheckHealthAsync();

        handler.LastCustomHeaders.ShouldContainKey("api-key");
        handler.LastCustomHeaders["api-key"].ShouldBe("my-api-key");
    }

    // -- 12. Respects cancellation token --

    [Test]
    public async Task CheckHealthAsync_CancellationToken_Respected()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var provider = CreateProvider(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await provider.CheckHealthAsync(cts.Token);

        // When cancelled, the provider catches the exception and returns unhealthy
        result.IsHealthy.ShouldBeFalse();
    }

    // -- Helpers --

    private static OpenAiProvider CreateProvider(
        HttpMessageHandler handler,
        string baseUrl = "https://api.openai.com/v1",
        string apiKey = "test-key")
    {
        var factory = new SingleHandlerHttpClientFactory(handler);
        return new OpenAiProvider(factory, baseUrl, apiKey);
    }
}

// ──────────────────────────────────────────────────────────────────────
//  Shared HTTP test fakes (used by both OpenAI and Gemini health check tests)
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// HTTP message handler that returns a configurable status code and body.
/// Also captures the last request URI and authorization header for assertions.
/// </summary>
internal sealed class ConfigurableHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;

    private readonly string _responseBody;

    public Uri? LastRequestUri { get; private set; }

    public string? LastAuthorizationHeader { get; private set; }

    public Dictionary<string, string> LastCustomHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurableHttpHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastRequestUri = request.RequestUri;

        if (request.Headers.Authorization is { } auth)
        {
            LastAuthorizationHeader = $"{auth.Scheme} {auth.Parameter}";
        }

        // Capture custom headers (non-standard)
        foreach (var header in request.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                LastCustomHeaders[header.Key] = string.Join(",", header.Value);
            }
        }

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// HTTP message handler that always throws a configured exception.
/// </summary>
internal sealed class ThrowingHttpHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw _exception;
    }
}

/// <summary>
/// IHttpClientFactory that returns HttpClients backed by a single handler.
/// </summary>
internal sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public SingleHandlerHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
