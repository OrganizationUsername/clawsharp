using Clawsharp.Core;
using Clawsharp.Providers.OpenRouter;
using NSubstitute;

namespace Clawsharp.Tests.Integration.E2E;

/// <summary>
/// Integration tests that hit the real OpenRouter API. Automatically skipped when
/// the <c>OPENROUTER_API_KEY</c> environment variable is not set.
/// Uses a cheap model (openai/gpt-4.1-nano) to minimize cost.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class OpenRouterIntegrationTests
{
    private const string TestModel = "openai/gpt-4.1-nano";

    private static readonly string? ApiKey =
        Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

    private OpenRouterProvider _provider = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return;
        }

        var factory = CreateHttpClientFactory();
        _provider = new OpenRouterProvider(factory, ApiKey, "openrouter-integration-test",
            httpReferer: "https://clawsharp-tests.local", appTitle: "clawsharp-tests");
    }

    [SetUp]
    public void SetUp()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            Assert.Ignore("OPENROUTER_API_KEY environment variable not set — skipping OpenRouter integration tests.");
        }
    }

    [Test]
    public async Task ChatAsync_ReturnsNonEmptyResponse()
    {
        var request = MakeChatRequest("What is 2 + 2? Reply with just the number.");

        var response = await _provider.ChatAsync(request);

        response.Content.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ChatAsync_ReportsTokenCounts()
    {
        var request = MakeChatRequest("What is 2 + 2? Reply with just the number.");

        var response = await _provider.ChatAsync(request);

        response.InputTokens.ShouldNotBeNull();
        response.InputTokens!.Value.ShouldBeGreaterThan(0);
        response.OutputTokens.ShouldNotBeNull();
        response.OutputTokens!.Value.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ChatAsync_ReportsCost()
    {
        var request = MakeChatRequest("Say hello.");

        var response = await _provider.ChatAsync(request);

        // OpenRouter reports cost for non-BYOK requests
        response.ProviderReportedCost.ShouldNotBeNull();
        response.ProviderReportedCost!.Value.ShouldBeGreaterThanOrEqualTo(0m);
    }

    [Test]
    public async Task ChatAsync_FinishReasonIsStop()
    {
        var request = MakeChatRequest("Say hello.");

        var response = await _provider.ChatAsync(request);

        response.FinishReason.ShouldBe(FinishReason.Stop);
    }

    [Test]
    public async Task StreamAsync_YieldsTextDeltas()
    {
        var request = MakeChatRequest("Count from 1 to 5, one per line.");

        var textParts = new List<string>();
        await foreach (var chunk in _provider.StreamAsync(request))
        {
            if (chunk is TextDeltaChunk delta)
            {
                textParts.Add(delta.Delta);
            }
        }

        textParts.ShouldNotBeEmpty();
        string.Join("", textParts).ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task StreamAsync_EndsWithDoneChunk()
    {
        var request = MakeChatRequest("Say hi.");

        StreamChunk? lastChunk = null;
        await foreach (var chunk in _provider.StreamAsync(request))
        {
            lastChunk = chunk;
        }

        lastChunk.ShouldBeOfType<StreamDoneChunk>();
    }

    [Test]
    public async Task StreamAsync_ReportsUsageWithCost()
    {
        var request = MakeChatRequest("What is 1 + 1?");

        StreamUsageChunk? usageChunk = null;
        await foreach (var chunk in _provider.StreamAsync(request))
        {
            if (chunk is StreamUsageChunk usage)
            {
                usageChunk = usage;
            }
        }

        // OpenRouter should report usage with cost in streaming mode
        if (usageChunk is not null)
        {
            usageChunk.InputTokens.ShouldBeGreaterThan(0);
            usageChunk.OutputTokens.ShouldBeGreaterThan(0);
            usageChunk.ProviderReportedCost.ShouldNotBeNull();
            usageChunk.ProviderReportedCost!.Value.ShouldBeGreaterThanOrEqualTo(0m);
        }
        else
        {
            Assert.Warn("OpenRouter did not report usage in streaming mode.");
        }
    }

    [Test]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var result = await _provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeTrue();
        result.ResponseTime.ShouldNotBeNull();
    }

    // ── Helpers ──

    private static ChatRequest MakeChatRequest(string userMessage) => new(
        Model: TestModel,
        Messages: [new ChatMessage(MessageRole.User, userMessage)],
        Temperature: 0.1f
    );

    private static IHttpClientFactory CreateHttpClientFactory()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        });
        return factory;
    }
}
