using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clawsharp.Channels.Web;
using Clawsharp.Config;
using Clawsharp.Config.Channels;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Integration.E2E;

/// <summary>
/// Integration tests for the Web channel's HTTP endpoints using an in-process Kestrel server.
/// Uses a FakeProvider-style background consumer to simulate AgentLoop processing.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class WebChannelIntegrationTests
{
    private const string StaticToken = "test-token-e2e-integration";

    private int _port;
    private WebChannel _webChannel = null!;
    private InMemoryMessageBus _bus = null!;
    private HttpClient _http = null!;
    private CancellationTokenSource _cts = null!;
    private Task? _consumerTask;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _port = Random.Shared.Next(30000, 40000);
        _bus = new InMemoryMessageBus();
        _cts = new CancellationTokenSource();

        var channelConfig = new ChannelConfig
        {
            Enabled = true,
            WebHost = "127.0.0.1",
            WebPort = _port,
            PairingToken = StaticToken
        };

        var appConfig = new AppConfig
        {
            Channels = new Dictionary<string, ChannelConfig>
            {
                ["web"] = channelConfig
            }
        };

        var appConfigOptions = Options.Create(appConfig);
        var pairingService = new WebPairingService(appConfigOptions, NullLogger<WebPairingService>.Instance);

        _webChannel = new WebChannel(
            appConfigOptions,
            _bus,
            pairingService,
            NullLogger<WebChannel>.Instance);

        await _webChannel.StartAsync(_cts.Token);
        await _webChannel.StartedAsync(_cts.Token);

        // Start a background consumer that reads from the bus and sends a canned reply
        _consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _bus.ReadAllAsync(_cts.Token))
                {
                    var reply = new OutboundMessage(
                        Channel: ChannelName.Web,
                        RecipientId: msg.SenderId,
                        Text: $"Echo: {msg.Text}");

                    await _webChannel.SendAsync(reply, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }, _cts.Token);

        // Create HTTP client targeting the in-process server
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _cts.CancelAsync();
        _http.Dispose();

        try
        {
            if (_consumerTask is not null)
            {
                await _consumerTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            // Best-effort wait
        }

        await _webChannel.StopAsync(CancellationToken.None);
        await _webChannel.DisposeAsync();
        _cts.Dispose();
    }

    [Test]
    public async Task PostChat_WithValidToken_ReturnsReply()
    {
        using var request = CreateChatRequest("Hello, world!", StaticToken);

        using var response = await _http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var reply = doc.RootElement.GetProperty("reply").GetString();
        reply.ShouldNotBeNullOrWhiteSpace();
        reply!.ShouldContain("Echo: Hello, world!");
    }

    [Test]
    public async Task PostChat_WithoutToken_Returns401()
    {
        using var content = new StringContent("""{"message":"test"}""", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat") { Content = content };
        // No Authorization header

        using var response = await _http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PostChat_WithInvalidToken_Returns401()
    {
        using var request = CreateChatRequest("test", "wrong-token-value");

        using var response = await _http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PostChat_EmptyMessage_Returns400()
    {
        using var content = new StringContent("""{"message":""}""", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaticToken);

        using var response = await _http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostChat_ReturnsSessionId()
    {
        using var request1 = CreateChatRequest("First message", StaticToken);
        using var response1 = await _http.SendAsync(request1);
        response1.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body1 = await response1.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(body1);
        var sessionId1 = doc1.RootElement.GetProperty("session_id").GetString();

        using var request2 = CreateChatRequest("Second message", StaticToken);
        using var response2 = await _http.SendAsync(request2);
        response2.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body2 = await response2.Content.ReadAsStringAsync();
        using var doc2 = JsonDocument.Parse(body2);
        var sessionId2 = doc2.RootElement.GetProperty("session_id").GetString();

        sessionId1.ShouldNotBeNullOrWhiteSpace();
        // Same token should produce the same deterministic session ID
        sessionId1.ShouldBe(sessionId2);
    }

    [Test]
    public async Task GetHealth_FromLocalhost_Returns200()
    {
        using var response = await _http.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("ok");
    }

    [Test]
    public async Task GetRoot_ReturnsHtmlWithSecurityHeaders()
    {
        using var response = await _http.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        contentType.ShouldBe("text/html");

        // Verify security headers are present
        response.Headers.TryGetValues("X-Content-Type-Options", out var xcto);
        xcto.ShouldNotBeNull();
        xcto!.ShouldContain("nosniff");

        response.Headers.TryGetValues("X-Frame-Options", out var xfo);
        xfo.ShouldNotBeNull();
        xfo!.ShouldContain("DENY");

        response.Headers.TryGetValues("Content-Security-Policy", out var csp);
        csp.ShouldNotBeNull();
    }

    // ── Helpers ──

    private static HttpRequestMessage CreateChatRequest(string message, string token)
    {
        var json = JsonSerializer.Serialize(new { message });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/chat") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
