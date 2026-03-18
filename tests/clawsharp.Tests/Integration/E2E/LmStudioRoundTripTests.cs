using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Providers.OpenAi;
using NSubstitute;

namespace Clawsharp.Tests.Integration.E2E;

/// <summary>
/// End-to-end integration tests that hit a real LM Studio instance at http://127.0.0.1:1234.
/// Tests are automatically skipped when LM Studio is not available.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class LmStudioRoundTripTests
{
    // LM Studio default; override with OLLAMA_HOST for CI (Ollama serves OpenAI-compat at /v1)
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } host
            ? host.TrimEnd('/') + "/v1"
            : "http://127.0.0.1:1234/v1";

    private static bool _lmStudioAvailable;
    private static string _modelName = "unknown";
    private HttpClient _http = null!;
    private OpenAiProvider _provider = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var response = await _http.GetAsync($"{BaseUrl}/models");
            _lmStudioAvailable = response.IsSuccessStatusCode;

            if (_lmStudioAvailable)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0)
                {
                    _modelName = data[0].GetProperty("id").GetString() ?? "unknown";
                }
                else
                {
                    // No models loaded — can't run tests
                    _lmStudioAvailable = false;
                }
            }
        }
        catch
        {
            _lmStudioAvailable = false;
        }

        if (_lmStudioAvailable)
        {
            var httpFactory = CreateHttpClientFactory();
            _provider = new OpenAiProvider(httpFactory, BaseUrl, "", "lmstudio");
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _http?.Dispose();

    [SetUp]
    public void SetUp()
    {
        if (!_lmStudioAvailable)
        {
            Assert.Ignore($"LLM server not available at {BaseUrl} (set OLLAMA_HOST to override)");
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
    public async Task ChatAsync_FinishReasonIsStop()
    {
        var request = MakeChatRequest("Say hello.");

        var response = await _provider.ChatAsync(request);

        response.FinishReason.ShouldBe(FinishReason.Stop);
    }

    [Test]
    public async Task StreamAsync_YieldsTextDeltas()
    {
        var request = MakeChatRequest("Count from 1 to 5.");

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
    public async Task StreamAsync_ReportsUsage()
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

        // LM Studio may or may not report usage in streaming mode —
        // some backends omit the usage chunk. If present, validate it.
        if (usageChunk is not null)
        {
            usageChunk.InputTokens.ShouldBeGreaterThan(0);
            usageChunk.OutputTokens.ShouldBeGreaterThan(0);
        }
        else
        {
            Assert.Warn("LM Studio did not report usage in streaming mode — this is backend-dependent.");
        }
    }

    [Test]
    public async Task StreamAsync_ReassembledTextContainsSpaces()
    {
        var request = MakeChatRequest("Write a short sentence about the weather.");

        var textParts = new List<string>();
        await foreach (var chunk in _provider.StreamAsync(request))
        {
            if (chunk is TextDeltaChunk delta)
            {
                textParts.Add(delta.Delta);
            }
        }

        textParts.ShouldNotBeEmpty();

        // Reassemble the full text from streaming tokens
        var fullText = string.Join("", textParts).Trim();
        fullText.ShouldNotBeNullOrWhiteSpace();

        // The reassembled text should contain spaces between words.
        // A multi-word response about weather should have at least a few spaces.
        var spaceCount = fullText.Count(c => c == ' ');
        spaceCount.ShouldBeGreaterThan(2,
            $"Expected spaces between words in streamed response but got: \"{fullText}\"");
    }

    // ── Helpers ──

    private ChatRequest MakeChatRequest(string userMessage) => new(
        Model: _modelName,
        Messages: [new ChatMessage(MessageRole.User, userMessage)],
        Temperature: 0.1f
    );

    /// <summary>
    /// Creates an IHttpClientFactory that returns a real HttpClient with a generous timeout.
    /// </summary>
    private static IHttpClientFactory CreateHttpClientFactory()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        });
        return factory;
    }
}
