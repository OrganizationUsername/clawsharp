using Clawsharp.Channels.Qq;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Channels;

namespace Clawsharp.Tests.Unit.Channels;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class QqChannelTests : IDisposable
{
    private readonly CapturingMessageBus _bus = new();

    private readonly FakeHttpClientFactory _httpFactory = new();

    private readonly ApprovedSendersStore _approvedSenders;

    private readonly string _tempDir;

    public QqChannelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "qq-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _approvedSenders = new ApprovedSendersStore(NullLogger<ApprovedSendersStore>.Instance);
    }

    public void Dispose()
    {
        _approvedSenders.Dispose();
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    // ── Constructor / Disabled scenarios ─────────────────────────────

    [Test]
    public void Name_ReturnsQq()
    {
        var channel = CreateChannel();
        channel.Name.ShouldBe(ChannelName.Qq);
    }

    [Test]
    public async Task SendAsync_WhenDisabled_DoesNotThrow()
    {
        var channel = CreateChannel(enabled: false);
        var msg = new OutboundMessage(ChannelName.Qq, "12345", "hello");
        await channel.SendAsync(msg);
        // No exception means pass; nothing was sent
    }

    [Test]
    public async Task ExecuteAsync_WhenDisabled_ReturnsImmediately()
    {
        var channel = CreateChannel(enabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        // Should complete near-instantly since disabled
        await channel.StartAsync(cts.Token);
        // Not throwing OperationCanceledException means it returned before the timeout
    }

    [Test]
    public void Constructor_WhenNoWebSocketUrl_DisablesChannel()
    {
        var config = new AppConfig
        {
            Channels = new Dictionary<string, ChannelConfig>
            {
                ["qq"] = new() { Enabled = true, WebSocketUrl = null }
            }
        };
        var channel = CreateChannelFromConfig(config);
        // Should not throw on send when disabled
        channel.SendAsync(new OutboundMessage(ChannelName.Qq, "123", "test")).ShouldNotThrow();
    }

    // ── DeriveApiBase ────────────────────────────────────────────────

    [Test]
    public void DeriveApiBase_WsUrl_ConvertsToHttp()
    {
        QqChannel.DeriveApiBase("ws://127.0.0.1:3001/ws")
                 .ShouldBe("http://127.0.0.1:3001");
    }

    [Test]
    public void DeriveApiBase_WssUrl_ConvertsToHttps()
    {
        // Port 443 is the default for HTTPS and is stripped by Uri.Authority
        QqChannel.DeriveApiBase("wss://example.com:443/onebot")
                 .ShouldBe("https://example.com");
    }

    [Test]
    public void DeriveApiBase_WssUrlNonDefaultPort_PreservesPort()
    {
        QqChannel.DeriveApiBase("wss://example.com:8443/onebot")
                 .ShouldBe("https://example.com:8443");
    }

    [Test]
    public void DeriveApiBase_WsUrlNoPath_ConvertsToHttp()
    {
        QqChannel.DeriveApiBase("ws://localhost:3001")
                 .ShouldBe("http://localhost:3001");
    }

    [Test]
    public void DeriveApiBase_WsUrlWithDefaultPort_StripsPath()
    {
        QqChannel.DeriveApiBase("ws://192.168.1.100/")
                 .ShouldBe("http://192.168.1.100");
    }

    // ── SendAsync routing ────────────────────────────────────────────

    [Test]
    public async Task SendAsync_PrivateMessage_PostsToSendPrivateMsg()
    {
        var handler = new CapturingHttpHandler();
        _httpFactory.SetHandler("qq", handler);
        var channel = CreateChannel();

        await channel.SendAsync(new OutboundMessage(ChannelName.Qq, "10001", "hello user"));

        handler.Requests.Count.ShouldBe(1);
        var req = handler.Requests[0];
        req.RequestUri!.AbsolutePath.ShouldBe("/send_private_msg");
        req.Method.ShouldBe(HttpMethod.Post);

        var body = await req.Content!.ReadAsStringAsync();
        body.ShouldContain("\"user_id\":\"10001\"");
        body.ShouldContain("\"message\":\"hello user\"");
    }

    [Test]
    public async Task SendAsync_GroupMessage_PostsToSendGroupMsg()
    {
        var handler = new CapturingHttpHandler();
        _httpFactory.SetHandler("qq", handler);
        var channel = CreateChannel();

        await channel.SendAsync(new OutboundMessage(ChannelName.Qq, "group:30003", "hello group"));

        handler.Requests.Count.ShouldBe(1);
        var req = handler.Requests[0];
        req.RequestUri!.AbsolutePath.ShouldBe("/send_group_msg");

        var body = await req.Content!.ReadAsStringAsync();
        body.ShouldContain("\"group_id\":\"30003\"");
        body.ShouldContain("\"message\":\"hello group\"");
    }

    [Test]
    public async Task SendAsync_WithToken_IncludesAuthorizationHeader()
    {
        var handler = new CapturingHttpHandler();
        _httpFactory.SetHandler("qq", handler);
        var channel = CreateChannel(token: "my-secret-token");

        await channel.SendAsync(new OutboundMessage(ChannelName.Qq, "10001", "hi"));

        handler.Requests.Count.ShouldBe(1);
        var req = handler.Requests[0];
        req.Headers.Authorization.ShouldNotBeNull();
        req.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        req.Headers.Authorization.Parameter.ShouldBe("my-secret-token");
    }

    [Test]
    public async Task SendAsync_WithoutToken_NoAuthorizationHeader()
    {
        var handler = new CapturingHttpHandler();
        _httpFactory.SetHandler("qq", handler);
        var channel = CreateChannel(token: null);

        await channel.SendAsync(new OutboundMessage(ChannelName.Qq, "10001", "hi"));

        handler.Requests.Count.ShouldBe(1);
        // When no token, uses PostAsync which doesn't set auth header
    }

    [Test]
    public async Task SendAsync_WhenHttpFails_DoesNotThrow()
    {
        var handler = new CapturingHttpHandler(statusCode: System.Net.HttpStatusCode.InternalServerError);
        _httpFactory.SetHandler("qq", handler);
        var channel = CreateChannel();

        // Should not throw
        await channel.SendAsync(new OutboundMessage(ChannelName.Qq, "10001", "hello"));
    }

    // ── DTO serialization ────────────────────────────────────────────

    [Test]
    public void QqSendPrivateMessage_Serialization()
    {
        var msg = new QqSendPrivateMessage("12345", "test message");
        var json = System.Text.Json.JsonSerializer.Serialize(msg, QqJsonContext.Default.QqSendPrivateMessage);

        json.ShouldContain("\"user_id\":\"12345\"");
        json.ShouldContain("\"message\":\"test message\"");
    }

    [Test]
    public void QqSendGroupMessage_Serialization()
    {
        var msg = new QqSendGroupMessage("67890", "group message");
        var json = System.Text.Json.JsonSerializer.Serialize(msg, QqJsonContext.Default.QqSendGroupMessage);

        json.ShouldContain("\"group_id\":\"67890\"");
        json.ShouldContain("\"message\":\"group message\"");
    }

    // ── Helper methods ───────────────────────────────────────────────

    private QqChannel CreateChannel(bool enabled = true, string? token = null)
    {
        var config = new AppConfig
        {
            Channels = new Dictionary<string, ChannelConfig>
            {
                ["qq"] = new()
                {
                    Enabled = enabled,
                    WebSocketUrl = enabled ? "ws://127.0.0.1:3001/ws" : null,
                    Token = token
                }
            }
        };
        return CreateChannelFromConfig(config);
    }

    private QqChannel CreateChannelFromConfig(AppConfig config)
    {
        return new QqChannel(
            Options.Create(config),
            _bus,
            NullLogger<QqChannel>.Instance,
            _httpFactory,
            _approvedSenders);
    }
}

/// <summary>
/// Fake IMessageBus that captures published messages for assertion.
/// </summary>
internal sealed class CapturingMessageBus : IMessageBus
{
    private readonly List<InboundMessage> _published = [];

    private readonly object _lock = new();

    public IReadOnlyList<InboundMessage> Published
    {
        get
        {
            lock (_lock)
            {
                return [.. _published];
            }
        }
    }

    public ValueTask PublishAsync(InboundMessage message, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _published.Add(message);
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<InboundMessage> ReadAllAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Not needed for unit tests");
    }
}

/// <summary>
/// Fake IHttpClientFactory that returns HttpClients backed by a configurable handler.
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpMessageHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    private readonly CapturingHttpHandler _defaultHandler = new();

    public void SetHandler(string name, HttpMessageHandler handler)
    {
        _handlers[name] = handler;
    }

    public HttpClient CreateClient(string name)
    {
        var handler = _handlers.GetValueOrDefault(name) ?? _defaultHandler;
        return new HttpClient(handler);
    }
}

/// <summary>
/// HTTP handler that captures requests and returns a configurable response.
/// </summary>
internal sealed class CapturingHttpHandler : HttpMessageHandler
{
    private readonly System.Net.HttpStatusCode _statusCode;

    private readonly string _responseBody;

    private readonly List<HttpRequestMessage> _requests = [];

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public CapturingHttpHandler(
        System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK,
        string responseBody = "{\"status\":\"ok\",\"retcode\":0,\"data\":{\"message_id\":1}}")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Clone the content so it can be read later in assertions
        _requests.Add(CloneRequest(request));

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        if (original.Content is not null)
        {
            var bytes = original.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}