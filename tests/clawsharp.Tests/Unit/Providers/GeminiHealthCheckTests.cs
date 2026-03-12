using System.Net;
using Clawsharp.Providers.Gemini;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Unit tests for <see cref="GeminiProvider.CheckHealthAsync"/>.
/// Uses a fake HTTP message handler to simulate API responses without network calls.
/// </summary>
[TestFixture]
public sealed class GeminiHealthCheckTests
{
    // -- 1. Returns healthy when HTTP 200 --

    [Test]
    public async Task CheckHealthAsync_Http200_ReturnsHealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"models":[]}""");
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

    // -- 4. Returns unhealthy when HTTP 403 --

    [Test]
    public async Task CheckHealthAsync_Http403_ReturnsUnhealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.Forbidden, """{"error":"forbidden"}""");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("403");
    }

    // -- 5. Returns unhealthy when connection refused --

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

    // -- 6. Returns unhealthy when timeout --

    [Test]
    public async Task CheckHealthAsync_Timeout_ReturnsUnhealthy()
    {
        var handler = new ThrowingHttpHandler(new TaskCanceledException("The request was canceled due to timeout"));
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
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"models":[]}""");
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

    // -- 9. Correct URL is called with API key as query parameter --

    [Test]
    public async Task CheckHealthAsync_CallsModelsEndpointWithApiKey()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"models":[]}""");
        var provider = CreateProvider(handler, apiKey: "test-gemini-key");

        await provider.CheckHealthAsync();

        handler.LastRequestUri.ShouldNotBeNull();
        var uri = handler.LastRequestUri!.ToString();
        uri.ShouldContain("generativelanguage.googleapis.com/v1beta/models");
        uri.ShouldContain("key=test-gemini-key");
    }

    // -- 10. Uses GET method --

    [Test]
    public async Task CheckHealthAsync_UsesGetMethod()
    {
        var handler = new MethodCapturingHttpHandler(HttpStatusCode.OK, """{"models":[]}""");
        var provider = CreateProvider(handler);

        await provider.CheckHealthAsync();

        handler.LastMethod.ShouldBe(HttpMethod.Get);
    }

    // -- 11. Respects cancellation token --

    [Test]
    public async Task CheckHealthAsync_CancellationToken_Respected()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"models":[]}""");
        var provider = CreateProvider(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await provider.CheckHealthAsync(cts.Token);

        // When cancelled, the provider catches the exception and returns unhealthy
        result.IsHealthy.ShouldBeFalse();
    }

    // -- 12. HTTP 503 returns unhealthy with status and reason phrase --

    [Test]
    public async Task CheckHealthAsync_Http503_MessageContainsStatusCode()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.ServiceUnavailable, "");
        var provider = CreateProvider(handler);

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("503");
    }

    // -- Helpers --

    private static GeminiProvider CreateProvider(
        HttpMessageHandler handler,
        string apiKey = "test-key")
    {
        var factory = new SingleHandlerHttpClientFactory(handler);
        return new GeminiProvider(factory, apiKey);
    }
}

/// <summary>
/// HTTP handler that also captures the HTTP method used.
/// </summary>
internal sealed class MethodCapturingHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;

    private readonly string _responseBody;

    public HttpMethod? LastMethod { get; private set; }

    public MethodCapturingHttpHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastMethod = request.Method;

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
