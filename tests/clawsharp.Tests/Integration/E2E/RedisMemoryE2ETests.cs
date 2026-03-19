using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Memory.Redis;
using Clawsharp.Providers.OpenAi;
using Clawsharp.Tools.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Clawsharp.Tests.Integration.E2E;

/// <summary>
/// End-to-end integration tests that exercise the full pipeline:
/// LLM provider + Redis memory backend + memory tools.
/// Redis runs in a Testcontainer; LLM tests auto-skip when no local server is available.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class RedisMemoryE2ETests
{
    // LM Studio default; override with OLLAMA_HOST for CI (Ollama serves OpenAI-compat at /v1)
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } host
            ? host.TrimEnd('/') + "/v1"
            : "http://127.0.0.1:1234/v1";

    private static bool _llmAvailable;
    private static string _modelName = "unknown";

    // Redis
    private RedisContainer _redisContainer = null!;
    private IConnectionMultiplexer _redis = null!;
    private RedisMemory _memory = null!;

    // LLM
    private HttpClient _http = null!;
    private OpenAiProvider _provider = null!;

    // Tools (instantiated directly, no DI needed)
    private MemoryWriteTool _writeTool = null!;
    private MemoryReadTool _readTool = null!;
    private MemorySearchTool _searchTool = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Start Redis container
        _redisContainer = new RedisBuilder("redis/redis-stack:latest").Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(
            $"{_redisContainer.GetConnectionString()},allowAdmin=true");

        // Init schema via a throwaway write + clear
        _memory = CreateMemory();
        await _memory.AppendFactAsync("schema init");
        await _memory.ClearAsync();

        // Instantiate tools
        _writeTool = new MemoryWriteTool(_memory);
        _readTool = new MemoryReadTool(_memory);
        _searchTool = new MemorySearchTool(_memory);

        // Check LLM availability (same pattern as LmStudioRoundTripTests)
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var response = await _http.GetAsync($"{BaseUrl}/models");
            _llmAvailable = response.IsSuccessStatusCode;

            if (_llmAvailable)
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
                    _llmAvailable = false;
                }
            }
        }
        catch
        {
            _llmAvailable = false;
        }

        if (_llmAvailable)
        {
            var httpFactory = CreateHttpClientFactory();
            _provider = new OpenAiProvider(httpFactory, BaseUrl, "", "lmstudio");
        }
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _http?.Dispose();
        _redis.Dispose();
        await _redisContainer.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        // Flush all data between tests and re-init schema
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();

        // Re-create memory instance to reset internal state after flush
        _memory = CreateMemory();
        await _memory.AppendFactAsync("schema init");
        await _memory.ClearAsync();

        // Re-bind tools to the fresh memory instance
        _writeTool = new MemoryWriteTool(_memory);
        _readTool = new MemoryReadTool(_memory);
        _searchTool = new MemorySearchTool(_memory);
    }

    // ── Tool-Level Tests (no LLM required) ──

    [Test]
    public async Task MemoryWriteTool_PersistsToRedis()
    {
        var args = JsonDocument.Parse("""{"fact":"user loves C# programming"}""").RootElement;

        var result = await _writeTool.ExecuteAsync(args);

        result.ShouldBe("Saved: user loves C# programming");

        var facts = await _memory.ListFactsAsync();
        facts.Count.ShouldBe(1);
        facts[0].Content.ShouldBe("user loves C# programming");
    }

    [Test]
    public async Task MemoryReadTool_ReturnsStoredFacts()
    {
        await _memory.AppendFactAsync("fact alpha");
        await _memory.AppendFactAsync("fact beta");
        await _memory.AppendFactAsync("fact gamma");

        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _readTool.ExecuteAsync(args);

        result.ShouldContain("## Memory");
        result.ShouldContain("fact alpha");
        result.ShouldContain("fact beta");
        result.ShouldContain("fact gamma");
    }

    [Test]
    public async Task MemorySearchTool_FindsMatchingFact()
    {
        await _memory.AppendFactAsync("user drinks coffee every morning");
        await _memory.AppendFactAsync("user prefers dark mode");
        await _memory.AppendFactAsync("user has two cats");

        // Give RediSearch time to index
        await Task.Delay(200);

        var args = JsonDocument.Parse("""{"query":"coffee"}""").RootElement;

        var result = await _searchTool.ExecuteAsync(args);

        result.ShouldContain("coffee");
    }

    [Test]
    public async Task MemorySearchTool_NoMatch_ReturnsNotFound()
    {
        await _memory.AppendFactAsync("user drinks coffee every morning");
        await _memory.AppendFactAsync("user prefers dark mode");

        await Task.Delay(200);

        var args = JsonDocument.Parse("""{"query":"xyzzyplugh"}""").RootElement;

        var result = await _searchTool.ExecuteAsync(args);

        result.ShouldContain("No matching");
    }

    [Test]
    public async Task FullLoop_WriteSearchReadClearVerify()
    {
        // Write via tool
        var writeArgs = JsonDocument.Parse("""{"fact":"user's birthday is January 15th"}""").RootElement;
        var writeResult = await _writeTool.ExecuteAsync(writeArgs);
        writeResult.ShouldStartWith("Saved:");

        // Wait for RediSearch indexing
        await Task.Delay(200);

        // Search via tool
        var searchArgs = JsonDocument.Parse("""{"query":"birthday"}""").RootElement;
        var searchResult = await _searchTool.ExecuteAsync(searchArgs);
        searchResult.ShouldContain("birthday");
        searchResult.ShouldContain("January 15th");

        // Read via tool
        var readArgs = JsonDocument.Parse("{}").RootElement;
        var readResult = await _readTool.ExecuteAsync(readArgs);
        readResult.ShouldContain("## Memory");
        readResult.ShouldContain("birthday");

        // Clear memory
        await _memory.ClearAsync();

        // Read via tool — should be empty
        var emptyResult = await _readTool.ExecuteAsync(readArgs);
        emptyResult.ShouldBe("(memory is empty)");
    }

    [Test]
    public async Task ClearPreservesHistory_ToolVerification()
    {
        // Write fact via tool
        var writeArgs = JsonDocument.Parse("""{"fact":"user likes hiking"}""").RootElement;
        await _writeTool.ExecuteAsync(writeArgs);

        // Append history directly
        await _memory.AppendHistoryAsync("User discussed weekend plans");

        // Clear facts
        await _memory.ClearAsync();

        // Read via tool shows empty (facts cleared)
        var readArgs = JsonDocument.Parse("{}").RootElement;
        var readResult = await _readTool.ExecuteAsync(readArgs);
        readResult.ShouldBe("(memory is empty)");

        // But history still exists in Redis (WORM semantics)
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var summaries = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: "clawsharp:history:*"))
        {
            var keyStr = key.ToString();
            if (keyStr == "clawsharp:history:seq")
            {
                continue;
            }

            var summary = await db.HashGetAsync(key, "summary");
            if (!summary.IsNullOrEmpty)
            {
                summaries.Add(summary.ToString());
            }
        }

        summaries.ShouldContain("User discussed weekend plans");
    }

    // ── LLM + Redis Memory Tests (auto-skip when LLM unavailable) ──

    [Test]
    public async Task LlmWithRedisMemory_ResponseReferencesStoredFact()
    {
        if (!_llmAvailable)
        {
            Assert.Ignore($"LLM server not available at {BaseUrl} (set OLLAMA_HOST to override)");
        }

        // Store a fact in Redis
        await _memory.AppendFactAsync("user's favorite color is blue");

        // Get memory context to include in system prompt
        var context = await _memory.GetContextAsync();
        context.ShouldNotBeNull();

        var request = new ChatRequest(
            Model: _modelName,
            Messages:
            [
                new ChatMessage(MessageRole.System,
                    $"You are a helpful assistant. Answer questions using the provided memory.\n\n{context}"),
                new ChatMessage(MessageRole.User,
                    "What is my favorite color? Reply with just the color name, nothing else.")
            ],
            Temperature: 0.0f
        );

        var response = await _provider.ChatAsync(request);

        response.Content.ShouldNotBeNullOrWhiteSpace();
        response.Content!.ToLowerInvariant().ShouldContain("blue");
    }

    [Test]
    public async Task LlmWithRedisMemory_StreamingResponseReferencesStoredFact()
    {
        if (!_llmAvailable)
        {
            Assert.Ignore($"LLM server not available at {BaseUrl} (set OLLAMA_HOST to override)");
        }

        // Store a fact in Redis
        await _memory.AppendFactAsync("user's favorite color is blue");

        var context = await _memory.GetContextAsync();
        context.ShouldNotBeNull();

        var request = new ChatRequest(
            Model: _modelName,
            Messages:
            [
                new ChatMessage(MessageRole.System,
                    $"You are a helpful assistant. Answer questions using the provided memory.\n\n{context}"),
                new ChatMessage(MessageRole.User,
                    "What is my favorite color? Reply with just the color name, nothing else.")
            ],
            Temperature: 0.0f
        );

        var textParts = new List<string>();
        await foreach (var chunk in _provider.StreamAsync(request))
        {
            if (chunk is TextDeltaChunk delta)
            {
                textParts.Add(delta.Delta);
            }
        }

        textParts.ShouldNotBeEmpty();

        var fullText = string.Join("", textParts);
        fullText.ShouldNotBeNullOrWhiteSpace();
        fullText.ToLowerInvariant().ShouldContain("blue");
    }

    // ── Helpers ──

    private RedisMemory CreateMemory()
    {
        return new RedisMemory(
            _redis,
            NullLogger<RedisMemory>.Instance);
    }

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
