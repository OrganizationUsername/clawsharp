using System.Net;
using System.Text;
using System.Text.Json;
using Clawsharp.Config.Agent;
using Clawsharp.Core;
using Clawsharp.Providers;
using Clawsharp.Providers.OpenAi;
using Clawsharp.Providers.OpenRouter;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Comprehensive unit tests for <see cref="OpenRouterProvider"/>: JSON serialization,
/// request building, response mapping, streaming, health checks, and factory wiring.
/// </summary>
[TestFixture]
public sealed class OpenRouterProviderTests
{
    // ── JSON Serialization ─────────────────────────────────────────────────────

    [Test]
    public void Request_Serialization_IncludesOpenRouterSpecificFields()
    {
        var request = new OpenRouterRequest
        {
            Model = "anthropic/claude-sonnet-4",
            Messages = [new CompletionMessage { Role = "user", Content = "Hello" }],
            Temperature = 0.5f,
            Models = ["anthropic/claude-sonnet-4", "openai/gpt-4o"],
            Route = "fallback",
            Provider = new OpenRouterProviderPreferences
            {
                Order = ["Anthropic", "Google"],
                AllowFallbacks = true,
                RequireParameters = false
            },
            Plugins = [new OpenRouterPlugin { Id = "web-search", Enabled = true }],
            User = "user-123"
        };

        var json = JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.OpenRouterRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("model").GetString().ShouldBe("anthropic/claude-sonnet-4");
        root.GetProperty("models").GetArrayLength().ShouldBe(2);
        root.GetProperty("route").GetString().ShouldBe("fallback");
        root.GetProperty("provider").GetProperty("order").GetArrayLength().ShouldBe(2);
        root.GetProperty("provider").GetProperty("allow_fallbacks").GetBoolean().ShouldBeTrue();
        root.GetProperty("provider").GetProperty("require_parameters").GetBoolean().ShouldBeFalse();
        root.GetProperty("plugins").GetArrayLength().ShouldBe(1);
        root.GetProperty("plugins")[0].GetProperty("id").GetString().ShouldBe("web-search");
        root.GetProperty("user").GetString().ShouldBe("user-123");
    }

    [Test]
    public void Request_Serialization_OmitsNullOpenRouterFields()
    {
        var request = new OpenRouterRequest
        {
            Model = "openai/gpt-4o",
            Messages = [new CompletionMessage { Role = "user", Content = "Hi" }]
        };

        var json = JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.OpenRouterRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("models", out _).ShouldBeFalse();
        root.TryGetProperty("route", out _).ShouldBeFalse();
        root.TryGetProperty("provider", out _).ShouldBeFalse();
        root.TryGetProperty("plugins", out _).ShouldBeFalse();
        root.TryGetProperty("user", out _).ShouldBeFalse();
    }

    [Test]
    public void Response_Deserialization_FullFields()
    {
        const string json = """
        {
            "id": "gen-abc123",
            "model": "anthropic/claude-sonnet-4",
            "choices": [{
                "message": { "role": "assistant", "content": "Hello!" },
                "finish_reason": "stop",
                "native_finish_reason": "end_turn"
            }],
            "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 5,
                "total_tokens": 15,
                "prompt_tokens_details": {
                    "cached_tokens": 3,
                    "cache_write_tokens": 7
                },
                "completion_tokens_details": {
                    "reasoning_tokens": 2
                },
                "cost": 0.00042
            }
        }
        """;

        var response = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterResponse);

        response.ShouldNotBeNull();
        response.Id.ShouldBe("gen-abc123");
        response.Model.ShouldBe("anthropic/claude-sonnet-4");
        response.Choices.Count.ShouldBe(1);
        response.Choices[0].FinishReason.ShouldBe("stop");
        response.Choices[0].NativeFinishReason.ShouldBe("end_turn");
        response.Usage.ShouldNotBeNull();
        response.Usage!.PromptTokens.ShouldBe(10);
        response.Usage.CompletionTokens.ShouldBe(5);
        response.Usage.TotalTokens.ShouldBe(15);
        response.Usage.Cost.ShouldBe(0.00042m);
        response.Usage.PromptDetails.ShouldNotBeNull();
        response.Usage.PromptDetails!.CachedTokens.ShouldBe(3);
        response.Usage.PromptDetails.CacheWriteTokens.ShouldBe(7);
        response.Usage.CompletionDetails.ShouldNotBeNull();
        response.Usage.CompletionDetails!.ReasoningTokens.ShouldBe(2);
    }

    [Test]
    public void Response_Deserialization_MinimalFields()
    {
        const string json = """
        {
            "id": "gen-xyz",
            "choices": [{
                "message": { "role": "assistant", "content": "Hi" },
                "finish_reason": "stop"
            }],
            "usage": {
                "prompt_tokens": 5,
                "completion_tokens": 1,
                "total_tokens": 6
            }
        }
        """;

        var response = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterResponse);

        response.ShouldNotBeNull();
        response.Choices[0].NativeFinishReason.ShouldBeNull();
        response.Usage!.Cost.ShouldBeNull();
        response.Usage.PromptDetails.ShouldBeNull();
        response.Usage.CompletionDetails.ShouldBeNull();
        response.Usage.IsByok.ShouldBeNull();
    }

    [Test]
    public void StreamChunk_Deserialization_WithUsageCost()
    {
        const string json = """
        {
            "choices": [{
                "delta": { "content": "Hello" },
                "finish_reason": null,
                "native_finish_reason": null
            }],
            "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 20,
                "total_tokens": 30,
                "prompt_tokens_details": {
                    "cached_tokens": 5,
                    "cache_write_tokens": 3
                },
                "cost": 0.001
            }
        }
        """;

        var chunk = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterStreamChunk);

        chunk.ShouldNotBeNull();
        chunk.Choices.ShouldNotBeNull();
        chunk.Choices!.Count.ShouldBe(1);
        chunk.Choices[0].Delta!.Content.ShouldBe("Hello");
        chunk.Usage.ShouldNotBeNull();
        chunk.Usage!.Cost.ShouldBe(0.001m);
        chunk.Usage.PromptTokens.ShouldBe(10);
        chunk.Usage.CompletionTokens.ShouldBe(20);
        chunk.Usage.PromptDetails!.CachedTokens.ShouldBe(5);
        chunk.Usage.PromptDetails.CacheWriteTokens.ShouldBe(3);
    }

    [Test]
    public void StreamChoice_Deserialization_NativeFinishReason()
    {
        const string json = """
        {
            "choices": [{
                "delta": {},
                "finish_reason": "stop",
                "native_finish_reason": "end_turn"
            }]
        }
        """;

        var chunk = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterStreamChunk);

        chunk.ShouldNotBeNull();
        chunk.Choices![0].FinishReason.ShouldBe("stop");
        chunk.Choices[0].NativeFinishReason.ShouldBe("end_turn");
    }

    [Test]
    public void Plugin_RoundTrip()
    {
        var plugin = new OpenRouterPlugin { Id = "web-search", Enabled = true };

        var json = JsonSerializer.Serialize(plugin, OpenRouterJsonContext.Default.OpenRouterPlugin);
        var deserialized = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterPlugin);

        deserialized.ShouldNotBeNull();
        deserialized.Id.ShouldBe("web-search");
        deserialized.Enabled.ShouldBe(true);
    }

    [Test]
    public void Plugin_RoundTrip_NullEnabled_Omitted()
    {
        var plugin = new OpenRouterPlugin { Id = "response-healing" };

        var json = JsonSerializer.Serialize(plugin, OpenRouterJsonContext.Default.OpenRouterPlugin);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("id").GetString().ShouldBe("response-healing");
        doc.RootElement.TryGetProperty("enabled", out _).ShouldBeFalse();
    }

    [Test]
    public void ProviderPreferences_RoundTrip()
    {
        var prefs = new OpenRouterProviderPreferences
        {
            Order = ["Anthropic", "Google"],
            AllowFallbacks = false,
            RequireParameters = true,
            ZeroDataRetention = true,
            DataCollection = "deny"
        };

        var json = JsonSerializer.Serialize(prefs, OpenRouterJsonContext.Default.OpenRouterProviderPreferences);
        var deserialized = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterProviderPreferences);

        deserialized.ShouldNotBeNull();
        deserialized.Order.ShouldBe(["Anthropic", "Google"]);
        deserialized.AllowFallbacks.ShouldBe(false);
        deserialized.RequireParameters.ShouldBe(true);
        deserialized.ZeroDataRetention.ShouldBe(true);
        deserialized.DataCollection.ShouldBe("deny");
    }

    [Test]
    public void ProviderPreferences_NullFields_Omitted()
    {
        var prefs = new OpenRouterProviderPreferences();

        var json = JsonSerializer.Serialize(prefs, OpenRouterJsonContext.Default.OpenRouterProviderPreferences);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("order", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("allow_fallbacks", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("require_parameters", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("zdr", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("data_collection", out _).ShouldBeFalse();
    }

    [Test]
    public void ProviderPreferences_ZeroDataRetention_SerializesAsZdr()
    {
        var prefs = new OpenRouterProviderPreferences { ZeroDataRetention = true };

        var json = JsonSerializer.Serialize(prefs, OpenRouterJsonContext.Default.OpenRouterProviderPreferences);
        using var doc = JsonDocument.Parse(json);

        // C# property is ZeroDataRetention but wire format is "zdr"
        doc.RootElement.TryGetProperty("zdr", out var zdr).ShouldBeTrue();
        zdr.GetBoolean().ShouldBeTrue();
    }

    [Test]
    public void ProviderPreferences_ZeroDataRetention_DeserializesFromZdr()
    {
        const string json = """{"zdr":true,"data_collection":"deny"}""";

        var prefs = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterProviderPreferences);

        prefs.ShouldNotBeNull();
        prefs.ZeroDataRetention.ShouldBe(true);
        prefs.DataCollection.ShouldBe("deny");
    }

    [Test]
    public void ProviderPreferences_ZeroDataRetention_False_Serialized()
    {
        var prefs = new OpenRouterProviderPreferences { ZeroDataRetention = false };

        var json = JsonSerializer.Serialize(prefs, OpenRouterJsonContext.Default.OpenRouterProviderPreferences);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("zdr", out var zdr).ShouldBeTrue();
        zdr.GetBoolean().ShouldBeFalse();
    }

    // ── Request Building ───────────────────────────────────────────────────────

    [Test]
    public async Task ChatAsync_SendsCorrectRequestFields()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-1",
            "choices": [{ "message": { "role": "assistant", "content": "OK" }, "finish_reason": "stop" }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages: [new ChatMessage(MessageRole.User, "Hi")],
            Temperature: 0.3f
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        root.GetProperty("model").GetString().ShouldBe("openai/gpt-4o");
        root.GetProperty("temperature").GetSingle().ShouldBe(0.3f);
        root.GetProperty("messages").GetArrayLength().ShouldBe(1);
        root.GetProperty("messages")[0].GetProperty("role").GetString().ShouldBe("user");
        // Non-streaming: stream should not be present (default false, condition WhenWritingDefault)
        root.TryGetProperty("stream", out _).ShouldBeFalse();
    }

    [Test]
    public async Task ChatAsync_SetsAuthorizationHeader()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler, apiKey: "sk-openrouter-test-key");

        await provider.ChatAsync(MinimalChatRequest);

        handler.CapturedRequest.ShouldNotBeNull();
        handler.CapturedRequest!.Headers.Authorization.ShouldNotBeNull();
        handler.CapturedRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.CapturedRequest.Headers.Authorization.Parameter.ShouldBe("sk-openrouter-test-key");
    }

    [Test]
    public async Task ChatAsync_SetsHttpRefererAndXTitle()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler, httpReferer: "https://myapp.com", appTitle: "MyApp");

        await provider.ChatAsync(MinimalChatRequest);

        handler.CapturedRequest.ShouldNotBeNull();
        handler.CapturedRequest!.Headers.TryGetValues("HTTP-Referer", out var refererValues).ShouldBeTrue();
        refererValues!.First().ShouldBe("https://myapp.com");
        handler.CapturedRequest.Headers.TryGetValues("X-Title", out var titleValues).ShouldBeTrue();
        titleValues!.First().ShouldBe("MyApp");
    }

    [Test]
    public async Task ChatAsync_ExtraHeaders_AppliedExceptAuth()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var extras = new Dictionary<string, string>
        {
            ["X-Custom-Header"] = "custom-value",
            ["Authorization"] = "should-be-skipped",
            ["x-api-key"] = "also-skipped"
        };
        var provider = CreateProvider(handler, extraHeaders: extras);

        await provider.ChatAsync(MinimalChatRequest);

        handler.CapturedRequest.ShouldNotBeNull();
        handler.CapturedRequest!.Headers.TryGetValues("X-Custom-Header", out var vals).ShouldBeTrue();
        vals!.First().ShouldBe("custom-value");
        // Authorization should be set by Bearer, not overridden by extraHeaders
        handler.CapturedRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
    }

    [Test]
    public async Task ChatAsync_WithTools_SetsToolChoiceAuto()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages: [new ChatMessage(MessageRole.User, "Use a tool")],
            Tools: [new ToolDefinition("test_tool", "A test tool", """{"type":"object","properties":{}}""")]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;

        root.GetProperty("tools").GetArrayLength().ShouldBe(1);
        root.GetProperty("tool_choice").GetString().ShouldBe("auto");
    }

    [Test]
    public async Task ChatAsync_WithoutTools_OmitsToolChoice()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        await provider.ChatAsync(MinimalChatRequest);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);

        doc.RootElement.TryGetProperty("tool_choice", out _).ShouldBeFalse();
    }

    // ── Response Mapping ───────────────────────────────────────────────────────

    [Test]
    public async Task ChatAsync_MapsBasicResponse()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-1",
            "model": "openai/gpt-4o",
            "choices": [{
                "message": { "role": "assistant", "content": "Hello there!" },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 4, "total_tokens": 14 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.Content.ShouldBe("Hello there!");
        response.FinishReason.ShouldBe(FinishReason.Stop);
        response.InputTokens.ShouldBe(10);
        response.OutputTokens.ShouldBe(4);
        response.ToolCalls.ShouldBeNull();
    }

    [Test]
    public async Task ChatAsync_MapsToolCalls()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-2",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [{
                        "id": "call_abc",
                        "type": "function",
                        "function": { "name": "get_weather", "arguments": "{\"city\":\"London\"}" }
                    }]
                },
                "finish_reason": "tool_calls"
            }],
            "usage": { "prompt_tokens": 20, "completion_tokens": 10, "total_tokens": 30 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.FinishReason.ShouldBe(FinishReason.ToolCalls);
        response.ToolCalls.ShouldNotBeNull();
        response.ToolCalls!.Count.ShouldBe(1);
        response.ToolCalls[0].Id.ShouldBe("call_abc");
        response.ToolCalls[0].Name.ShouldBe("get_weather");
        response.ToolCalls[0].ArgumentsJson.ShouldBe("""{"city":"London"}""");
    }

    [Test]
    public async Task ChatAsync_CostPassthrough()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-3",
            "choices": [{ "message": { "role": "assistant", "content": "OK" }, "finish_reason": "stop" }],
            "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 50,
                "total_tokens": 150,
                "cost": 0.00123
            }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.ProviderReportedCost.ShouldBe(0.00123m);
    }

    [Test]
    public async Task ChatAsync_CacheTokens()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-4",
            "choices": [{ "message": { "role": "assistant", "content": "OK" }, "finish_reason": "stop" }],
            "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 20,
                "total_tokens": 120,
                "prompt_tokens_details": {
                    "cached_tokens": 80,
                    "cache_write_tokens": 15
                }
            }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.CacheReadTokens.ShouldBe(80);
        response.CacheWriteTokens.ShouldBe(15);
    }

    [Test]
    public async Task ChatAsync_NoCost_ProviderReportedCostIsNull()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-5",
            "choices": [{ "message": { "role": "assistant", "content": "OK" }, "finish_reason": "stop" }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.ProviderReportedCost.ShouldBeNull();
    }

    [TestCase("stop", "stop")]
    [TestCase("length", "length")]
    [TestCase("tool_calls", "tool_calls")]
    [TestCase("function_call", "tool_calls")]
    [TestCase("content_filter", "content_filter")]
    [TestCase("unknown_reason", "stop")] // Unknown defaults to Stop
    public async Task ChatAsync_FinishReason_MapsCorrectly(string apiReason, string expectedValue)
    {
        var json = $$"""
        {
            "id": "gen-fr",
            "choices": [{ "message": { "role": "assistant", "content": "OK" }, "finish_reason": "{{apiReason}}" }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """;
        var handler = new MockHandler(CreateJsonResponse(json));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.FinishReason.Value.ShouldBe(expectedValue);
    }

    [Test]
    public async Task ChatAsync_NativeFinishReason_DoesNotCrash()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-nfr",
            "choices": [{
                "message": { "role": "assistant", "content": "OK" },
                "finish_reason": "stop",
                "native_finish_reason": "end_turn"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 1, "total_tokens": 6 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.Content.ShouldBe("OK");
        response.FinishReason.ShouldBe(FinishReason.Stop);
    }

    [Test]
    public async Task ChatAsync_ReasoningContent_MappedCorrectly()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-rc",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "42",
                    "reasoning_content": "Let me think about this..."
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 10, "total_tokens": 15 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.Content.ShouldBe("42");
        response.ReasoningContent.ShouldBe("Let me think about this...");
    }

    [Test]
    public async Task ChatAsync_StripsThinkingBlocks()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-think",
            "choices": [{
                "message": { "role": "assistant", "content": "<think>internal reasoning</think>Hello!" },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 5, "total_tokens": 10 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.Content.ShouldBe("Hello!");
    }

    [Test]
    public async Task ChatAsync_EmptyResponse_Throws()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-empty",
            "choices": [],
            "usage": { "prompt_tokens": 5, "completion_tokens": 0, "total_tokens": 5 }
        }
        """));
        var provider = CreateProvider(handler);

        await Should.ThrowAsync<InvalidOperationException>(provider.ChatAsync(MinimalChatRequest));
    }

    [Test]
    public async Task ChatAsync_FinishReasonError_Throws()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-err",
            "choices": [{
                "finish_reason": "error",
                "message": { "role": "assistant", "content": "" },
                "error": { "code": 502, "message": "Provider disconnected" }
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 0, "total_tokens": 5 }
        }
        """));
        var provider = CreateProvider(handler);

        var ex = await Should.ThrowAsync<InvalidOperationException>(provider.ChatAsync(MinimalChatRequest));
        ex.Message.ShouldContain("Provider disconnected");
    }

    [Test]
    public async Task ChatAsync_ErrorWithoutFinishReason_Throws()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-err2",
            "choices": [{
                "finish_reason": "stop",
                "message": { "role": "assistant", "content": "" },
                "error": { "code": 403, "message": "Content moderation flagged" }
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 0, "total_tokens": 5 }
        }
        """));
        var provider = CreateProvider(handler);

        var ex = await Should.ThrowAsync<InvalidOperationException>(provider.ChatAsync(MinimalChatRequest));
        ex.Message.ShouldContain("Content moderation flagged");
    }

    // ── Streaming ──────────────────────────────────────────────────────────────

    [Test]
    public async Task StreamAsync_TextDeltas()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Hel"}}]}""",
            """{"choices":[{"delta":{"content":"lo!"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var textChunks = chunks.OfType<TextDeltaChunk>().ToList();
        textChunks.Count.ShouldBe(2);
        textChunks[0].Delta.ShouldBe("Hel");
        textChunks[1].Delta.ShouldBe("lo!");
    }

    [Test]
    public async Task StreamAsync_ToolCallChunks()
    {
        // Build SSE data with properly escaped JSON strings matching real OpenRouter output.
        // Each data line is a JSON object; the "arguments" field contains a JSON string fragment.
        var chunk1 = """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"get_weather","arguments":""}}]}}]}""";
        var chunk2 = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"city\\\":\"}}]}}]}";
        var chunk3 = "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"London\\\"}\"}}]}}]}";
        var sse = BuildSse(chunk1, chunk2, chunk3, "[DONE]");
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var toolChunks = chunks.OfType<ToolCallChunk>().ToList();
        toolChunks.Count.ShouldBe(3);
        toolChunks[0].Index.ShouldBe(0);
        toolChunks[0].Id.ShouldBe("call_1");
        toolChunks[0].Name.ShouldBe("get_weather");
        toolChunks[1].ArgumentsDelta.ShouldBe("{\"city\":");
        toolChunks[2].ArgumentsDelta.ShouldBe("\"London\"}");
    }

    [Test]
    public async Task StreamAsync_UsageWithCost()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Hi"}}]}""",
            """{"choices":[],"usage":{"prompt_tokens":100,"completion_tokens":20,"total_tokens":120,"prompt_tokens_details":{"cached_tokens":50,"cache_write_tokens":10},"cost":0.005}}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var usageChunks = chunks.OfType<StreamUsageChunk>().ToList();
        usageChunks.Count.ShouldBe(1);
        usageChunks[0].InputTokens.ShouldBe(100);
        usageChunks[0].OutputTokens.ShouldBe(20);
        usageChunks[0].CacheReadTokens.ShouldBe(50);
        usageChunks[0].CacheWriteTokens.ShouldBe(10);
        usageChunks[0].ProviderReportedCost.ShouldBe(0.005m);
    }

    [Test]
    public async Task StreamAsync_CacheTokensInStream()
    {
        var sse = BuildSse(
            """{"choices":[],"usage":{"prompt_tokens":50,"completion_tokens":10,"total_tokens":60,"prompt_tokens_details":{"cached_tokens":30,"cache_write_tokens":20}}}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var usage = chunks.OfType<StreamUsageChunk>().Single();
        usage.CacheReadTokens.ShouldBe(30);
        usage.CacheWriteTokens.ShouldBe(20);
    }

    [Test]
    public async Task StreamAsync_DoneSentinel_YieldsStreamDoneChunk()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Hi"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.Last().ShouldBeOfType<StreamDoneChunk>();
    }

    [Test]
    public async Task StreamAsync_SseComments_SilentlyIgnored()
    {
        // OpenRouter sends ": OPENROUTER PROCESSING" comment lines
        var sseText =
            ": OPENROUTER PROCESSING\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
            ": ping\n" +
            "data: [DONE]\n\n";
        var handler = new MockHandler(CreateSseResponse(sseText));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var textChunks = chunks.OfType<TextDeltaChunk>().ToList();
        textChunks.Count.ShouldBe(1);
        textChunks[0].Delta.ShouldBe("Hello");
    }

    [Test]
    public async Task StreamAsync_MalformedJson_SkippedGracefully()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Before"}}]}""",
            "not valid json at all",
            """{"choices":[{"delta":{"content":"After"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var textChunks = chunks.OfType<TextDeltaChunk>().ToList();
        textChunks.Count.ShouldBe(2);
        textChunks[0].Delta.ShouldBe("Before");
        textChunks[1].Delta.ShouldBe("After");
    }

    [Test]
    public async Task StreamAsync_ReasoningContent_YieldsThinkingDeltaChunk()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"reasoning_content":"Let me think..."}}]}""",
            """{"choices":[{"delta":{"content":"42"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.OfType<ThinkingDeltaChunk>().Single().Delta.ShouldBe("Let me think...");
        chunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("42");
    }

    [Test]
    public async Task StreamAsync_EmptyDelta_NoChunkYielded()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{}}]}""",
            """{"choices":[{"delta":{"content":"Real"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var textChunks = chunks.OfType<TextDeltaChunk>().ToList();
        textChunks.Count.ShouldBe(1);
        textChunks[0].Delta.ShouldBe("Real");
    }

    [Test]
    public async Task StreamAsync_NullChoices_SkippedGracefully()
    {
        var sse = BuildSse(
            """{"choices":null}""",
            """{"choices":[{"delta":{"content":"OK"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("OK");
    }

    [Test]
    public async Task StreamAsync_RequestSendsStreamTrue()
    {
        var sse = BuildSse("[DONE]");
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        await CollectStreamChunks(provider);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        doc.RootElement.GetProperty("stream").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task StreamAsync_UsageWithoutCost_ProviderReportedCostIsNull()
    {
        var sse = BuildSse(
            """{"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var usage = chunks.OfType<StreamUsageChunk>().Single();
        usage.ProviderReportedCost.ShouldBeNull();
    }

    // ── Mid-Stream Error Handling ────────────────────────────────────────────

    [Test]
    public async Task StreamAsync_MidStreamError_Throws()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Partial"}}]}""",
            """{"error":{"code":502,"message":"Provider disconnected"},"choices":[{"delta":{"content":""},"finish_reason":"error"}]}"""
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(MinimalChatRequest)) { }
        });
        ex.Message.ShouldContain("Provider disconnected");
    }

    [Test]
    public async Task StreamAsync_FinishReasonError_WithoutTopLevelError_Throws()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"delta":{"content":""},"finish_reason":"error"}]}"""
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(MinimalChatRequest)) { }
        });
        ex.Message.ShouldContain("error finish reason");
    }

    [Test]
    public void OpenRouterError_Deserialization()
    {
        const string json = """{"code":403,"message":"Content moderation flagged"}""";
        var error = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterError);

        error.ShouldNotBeNull();
        error.Code.ShouldBe(403);
        error.Message.ShouldBe("Content moderation flagged");
    }

    // ── Key Info DTO Serialization ────────────────────────────────────────────

    [Test]
    public void KeyInfoResponse_Deserialization_FullFields()
    {
        const string json = """
        {
            "data": {
                "label": "my-production-key",
                "limit": 100.00,
                "limit_remaining": 87.5432,
                "usage": 12.4568,
                "usage_daily": 1.2345,
                "usage_monthly": 8.9012,
                "is_free_tier": false
            }
        }
        """;

        var response = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterKeyInfoResponse);

        response.ShouldNotBeNull();
        response.Data.ShouldNotBeNull();
        response.Data!.Label.ShouldBe("my-production-key");
        response.Data.Limit.ShouldBe(100.00m);
        response.Data.LimitRemaining.ShouldBe(87.5432m);
        response.Data.Usage.ShouldBe(12.4568m);
        response.Data.UsageDaily.ShouldBe(1.2345m);
        response.Data.UsageMonthly.ShouldBe(8.9012m);
        response.Data.IsFreeTier.ShouldBeFalse();
    }

    [Test]
    public void KeyInfoResponse_Deserialization_FreeTier_NullLimits()
    {
        const string json = """
        {
            "data": {
                "label": "",
                "limit": null,
                "limit_remaining": null,
                "usage": 0,
                "usage_daily": 0,
                "usage_monthly": 0,
                "is_free_tier": true
            }
        }
        """;

        var response = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterKeyInfoResponse);

        response.ShouldNotBeNull();
        response.Data.ShouldNotBeNull();
        response.Data!.Limit.ShouldBeNull();
        response.Data.LimitRemaining.ShouldBeNull();
        response.Data.IsFreeTier.ShouldBeTrue();
    }

    [Test]
    public void KeyInfoResponse_Deserialization_NullData()
    {
        const string json = """{"data": null}""";

        var response = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterKeyInfoResponse);

        response.ShouldNotBeNull();
        response.Data.ShouldBeNull();
    }

    // ── GetKeyInfoAsync ───────────────────────────────────────────────────────

    [Test]
    public async Task GetKeyInfoAsync_ParsesValidResponse()
    {
        const string keyInfoJson = """
        {
            "data": {
                "label": "test-key-label",
                "limit": 50.00,
                "limit_remaining": 42.1234,
                "usage": 7.8766,
                "usage_daily": 0.5432,
                "usage_monthly": 3.2100,
                "is_free_tier": false
            }
        }
        """;
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, keyInfoJson);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var keyInfo = await provider.GetKeyInfoAsync();

        keyInfo.ShouldNotBeNull();
        keyInfo.Label.ShouldBe("test-key-label");
        keyInfo.Limit.ShouldBe(50.00m);
        keyInfo.LimitRemaining.ShouldBe(42.1234m);
        keyInfo.Usage.ShouldBe(7.8766m);
        keyInfo.UsageDaily.ShouldBe(0.5432m);
        keyInfo.UsageMonthly.ShouldBe(3.2100m);
        keyInfo.IsFreeTier.ShouldBeFalse();
    }

    [Test]
    public async Task GetKeyInfoAsync_ReturnsNull_OnHttpError()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.Unauthorized, """{"error":"invalid key"}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "bad-key");

        var keyInfo = await provider.GetKeyInfoAsync();

        keyInfo.ShouldBeNull();
    }

    [Test]
    public async Task GetKeyInfoAsync_ReturnsNull_OnException()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused"));
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "key");

        var keyInfo = await provider.GetKeyInfoAsync();

        keyInfo.ShouldBeNull();
    }

    [Test]
    public async Task GetKeyInfoAsync_CallsKeyEndpoint()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":{"usage":0,"usage_daily":0,"usage_monthly":0,"is_free_tier":true}}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "key");

        await provider.GetKeyInfoAsync();

        handler.LastRequestUri.ShouldNotBeNull();
        handler.LastRequestUri!.ToString().ShouldBe("https://openrouter.ai/api/v1/key");
    }

    // ── Health Check ───────────────────────────────────────────────────────────

    [Test]
    public async Task CheckHealthAsync_Http200_WithCredits_ReturnsHealthyWithCredits()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK,
            """{"data":{"limit_remaining":42.50,"usage":7.50,"usage_daily":1.00,"usage_monthly":5.00,"is_free_tier":false}}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeTrue();
        result.Message.ShouldContain("$42.50");
        result.Message.ShouldContain("credits remaining");
        result.ResponseTime.ShouldNotBeNull();
    }

    [Test]
    public async Task CheckHealthAsync_Http200_WithoutCreditsField_ReturnsGenericHealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK,
            """{"data":{"usage":0,"usage_daily":0,"usage_monthly":0,"is_free_tier":true}}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeTrue();
        result.Message.ShouldBe("HTTP 200");
    }

    [Test]
    public async Task CheckHealthAsync_Http401_ReturnsUnhealthy()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.Unauthorized, """{"error":"invalid key"}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "bad-key");

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("401");
    }

    [Test]
    public async Task CheckHealthAsync_Exception_ReturnsUnhealthy()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("DNS failed"));
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "key");

        var result = await provider.CheckHealthAsync();

        result.IsHealthy.ShouldBeFalse();
        result.Message!.ShouldContain("DNS failed");
        result.ResponseTime.ShouldNotBeNull();
    }

    [Test]
    public async Task CheckHealthAsync_CallsKeyEndpoint()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":{"usage":0,"usage_daily":0,"usage_monthly":0,"is_free_tier":true}}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "key");

        await provider.CheckHealthAsync();

        handler.LastRequestUri.ShouldNotBeNull();
        handler.LastRequestUri!.ToString().ShouldBe("https://openrouter.ai/api/v1/key");
    }

    [Test]
    public async Task CheckHealthAsync_SetsAuthorizationHeader()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":{}}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "sk-or-test-123");

        await provider.CheckHealthAsync();

        handler.LastAuthorizationHeader.ShouldBe("Bearer sk-or-test-123");
    }

    // ── ProviderFactory Wiring ─────────────────────────────────────────────────

    [Test]
    public void ProviderFactory_OpenRouterType_ReturnsOpenRouterProvider()
    {
        var configs = new Dictionary<string, ProviderConfig>
        {
            ["my-openrouter"] = new()
            {
                Type = "openrouter",
                ApiKey = "sk-or-test"
            }
        };
        var factory = new SingleHandlerHttpClientFactory(
            new ConfigurableHttpHandler(HttpStatusCode.OK, ""));

        var provider = ProviderFactory.Create("my-openrouter", configs, factory);

        provider.ShouldBeOfType<OpenRouterProvider>();
        provider.Name.ShouldBe("my-openrouter");
    }

    [Test]
    public void ProviderFactory_ExtractsHttpRefererAndXTitle()
    {
        var configs = new Dictionary<string, ProviderConfig>
        {
            ["openrouter"] = new()
            {
                Type = "openrouter",
                ApiKey = "sk-or-test",
                ExtraHeaders = new Dictionary<string, string>
                {
                    ["HTTP-Referer"] = "https://myapp.example.com",
                    ["X-Title"] = "My App",
                    ["X-Custom"] = "kept"
                }
            }
        };
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var httpFactory = new SingleHandlerHttpClientFactory(handler);

        var provider = ProviderFactory.Create("openrouter", configs, httpFactory);

        // Verify headers are set by making a health check call
        provider.ShouldBeOfType<OpenRouterProvider>();
        var orProvider = (IHealthCheckableProvider)provider;
        orProvider.CheckHealthAsync().GetAwaiter().GetResult();

        handler.LastCustomHeaders.ShouldContainKey("HTTP-Referer");
        handler.LastCustomHeaders["HTTP-Referer"].ShouldBe("https://myapp.example.com");
        handler.LastCustomHeaders.ShouldContainKey("X-Title");
        handler.LastCustomHeaders["X-Title"].ShouldBe("My App");
        handler.LastCustomHeaders.ShouldContainKey("X-Custom");
        handler.LastCustomHeaders["X-Custom"].ShouldBe("kept");
    }

    [Test]
    public void ProviderFactory_OpenRouter_EmptyExtraHeaders_NoRefererOrTitle()
    {
        var configs = new Dictionary<string, ProviderConfig>
        {
            ["openrouter"] = new()
            {
                Type = "openrouter",
                ApiKey = "key"
            }
        };
        var httpFactory = new SingleHandlerHttpClientFactory(
            new ConfigurableHttpHandler(HttpStatusCode.OK, ""));

        var provider = ProviderFactory.Create("openrouter", configs, httpFactory);

        provider.ShouldBeOfType<OpenRouterProvider>();
    }

    // ── Vision Support ─────────────────────────────────────────────────────────

    [Test]
    public void SupportsVision_ReturnsTrue()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        provider.SupportsVision.ShouldBeTrue();
    }

    // ── Provider Name ──────────────────────────────────────────────────────────

    [Test]
    public void Name_DefaultsToOpenrouter()
    {
        var factory = new SingleHandlerHttpClientFactory(
            new ConfigurableHttpHandler(HttpStatusCode.OK, ""));
        var provider = new OpenRouterProvider(factory, "key");

        provider.Name.ShouldBe("openrouter");
    }

    [Test]
    public void Name_CustomName()
    {
        var factory = new SingleHandlerHttpClientFactory(
            new ConfigurableHttpHandler(HttpStatusCode.OK, ""));
        var provider = new OpenRouterProvider(factory, "key", name: "my-or");

        provider.Name.ShouldBe("my-or");
    }

    // ── API Key Rotation ───────────────────────────────────────────────────────

    [Test]
    public async Task ChatAsync_ApiKeyRotation()
    {
        var keys = new List<string> { "key-1", "key-2", "key-3" };
        var handler = new FreshResponseHandler(MinimalResponse);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "unused-primary", "openrouter", apiKeys: keys);

        // First call
        await provider.ChatAsync(MinimalChatRequest);
        var firstAuth = handler.CapturedRequests[0].Headers.Authorization!.Parameter;

        // Second call
        await provider.ChatAsync(MinimalChatRequest);
        var secondAuth = handler.CapturedRequests[1].Headers.Authorization!.Parameter;

        // Keys should rotate (first two from the list)
        firstAuth.ShouldBe("key-1");
        secondAuth.ShouldBe("key-2");
    }

    // ── Model Listing ──────────────────────────────────────────────────────────

    private const string ModelsResponseJson = """
    {
        "data": [
            {
                "id": "openai/gpt-4o",
                "name": "GPT-4o",
                "context_length": 128000,
                "pricing": { "prompt": "0.0000025", "completion": "0.00001" },
                "architecture": { "input_modalities": ["text", "image"], "output_modalities": ["text"] },
                "top_provider": { "context_length": 128000, "max_completion_tokens": 16384, "is_moderated": true }
            },
            {
                "id": "anthropic/claude-sonnet-4",
                "name": "Claude Sonnet 4",
                "context_length": 200000,
                "pricing": { "prompt": "0.000003", "completion": "0.000015" },
                "architecture": { "input_modalities": ["text", "image"], "output_modalities": ["text"] },
                "top_provider": { "context_length": 200000, "max_completion_tokens": 64000, "is_moderated": false }
            }
        ]
    }
    """;

    [Test]
    public async Task ListModelsAsync_ParsesResponse()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, ModelsResponseJson);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var models = await provider.ListModelsAsync();

        models.Count.ShouldBe(2);

        models[0].Id.ShouldBe("openai/gpt-4o");
        models[0].Name.ShouldBe("GPT-4o");
        models[0].ContextLength.ShouldBe(128000);
        models[0].Pricing.ShouldNotBeNull();
        models[0].Pricing!.Prompt.ShouldBe("0.0000025");
        models[0].Pricing.Completion.ShouldBe("0.00001");
        models[0].Architecture.ShouldNotBeNull();
        models[0].Architecture!.InputModalities.ShouldBe(["text", "image"]);
        models[0].Architecture.OutputModalities.ShouldBe(["text"]);
        models[0].TopProvider.ShouldNotBeNull();
        models[0].TopProvider!.ContextLength.ShouldBe(128000);
        models[0].TopProvider.MaxCompletionTokens.ShouldBe(16384);
        models[0].TopProvider.IsModerated.ShouldBeTrue();

        models[1].Id.ShouldBe("anthropic/claude-sonnet-4");
        models[1].Name.ShouldBe("Claude Sonnet 4");
        models[1].ContextLength.ShouldBe(200000);
        models[1].TopProvider!.IsModerated.ShouldBeFalse();
    }

    [Test]
    public async Task ListModelsAsync_ReturnsEmpty_OnHttpError()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.InternalServerError, """{"error":"server error"}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var models = await provider.ListModelsAsync();

        models.Count.ShouldBe(0);
    }

    [Test]
    public async Task ListModelsAsync_ReturnsEmpty_OnException()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused"));
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var models = await provider.ListModelsAsync();

        models.Count.ShouldBe(0);
    }

    [Test]
    public async Task ListModelsAsync_CallsModelsEndpoint()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        await provider.ListModelsAsync();

        handler.LastRequestUri.ShouldNotBeNull();
        handler.LastRequestUri!.ToString().ShouldBe("https://openrouter.ai/api/v1/models");
    }

    [Test]
    public async Task ListModelsAsync_SetsAuthorizationHeader()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, """{"data":[]}""");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "sk-or-models-key");

        await provider.ListModelsAsync();

        handler.LastAuthorizationHeader.ShouldBe("Bearer sk-or-models-key");
    }

    [Test]
    public async Task GetModelContextLengthAsync_FindsModel()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, ModelsResponseJson);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var contextLength = await provider.GetModelContextLengthAsync("anthropic/claude-sonnet-4");

        contextLength.ShouldBe(200000);
    }

    [Test]
    public async Task GetModelContextLengthAsync_CaseInsensitive()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, ModelsResponseJson);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var contextLength = await provider.GetModelContextLengthAsync("OpenAI/GPT-4O");

        contextLength.ShouldBe(128000);
    }

    [Test]
    public async Task GetModelContextLengthAsync_ReturnsNull_ForUnknownModel()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, ModelsResponseJson);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var contextLength = await provider.GetModelContextLengthAsync("nonexistent/model");

        contextLength.ShouldBeNull();
    }

    [Test]
    public async Task GetModelContextLengthAsync_ReturnsNull_OnFetchFailure()
    {
        var handler = new ConfigurableHttpHandler(HttpStatusCode.InternalServerError, "");
        var factory = new SingleHandlerHttpClientFactory(handler);
        var provider = new OpenRouterProvider(factory, "test-key");

        var contextLength = await provider.GetModelContextLengthAsync("openai/gpt-4o");

        contextLength.ShouldBeNull();
    }

    [Test]
    public void OpenRouterModel_Deserialization_AllFields()
    {
        const string json = """
        {
            "id": "openai/gpt-4o",
            "name": "GPT-4o",
            "context_length": 128000,
            "pricing": { "prompt": "0.0000025", "completion": "0.00001" },
            "architecture": { "input_modalities": ["text", "image"], "output_modalities": ["text"] },
            "top_provider": { "context_length": 128000, "max_completion_tokens": 16384, "is_moderated": true }
        }
        """;

        var model = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterModel);

        model.ShouldNotBeNull();
        model.Id.ShouldBe("openai/gpt-4o");
        model.Name.ShouldBe("GPT-4o");
        model.ContextLength.ShouldBe(128000);
        model.Pricing.ShouldNotBeNull();
        model.Pricing!.Prompt.ShouldBe("0.0000025");
        model.Pricing.Completion.ShouldBe("0.00001");
        model.Architecture.ShouldNotBeNull();
        model.Architecture!.InputModalities.ShouldBe(["text", "image"]);
        model.Architecture.OutputModalities.ShouldBe(["text"]);
        model.TopProvider.ShouldNotBeNull();
        model.TopProvider!.ContextLength.ShouldBe(128000);
        model.TopProvider.MaxCompletionTokens.ShouldBe(16384);
        model.TopProvider.IsModerated.ShouldBeTrue();
    }

    [Test]
    public void OpenRouterModel_Deserialization_MinimalFields()
    {
        const string json = """
        {
            "id": "meta/llama-3-70b",
            "name": "Llama 3 70B"
        }
        """;

        var model = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterModel);

        model.ShouldNotBeNull();
        model.Id.ShouldBe("meta/llama-3-70b");
        model.Name.ShouldBe("Llama 3 70B");
        model.ContextLength.ShouldBeNull();
        model.Pricing.ShouldBeNull();
        model.Architecture.ShouldBeNull();
        model.TopProvider.ShouldBeNull();
    }

    [Test]
    public void OpenRouterModelsResponse_Deserialization_EmptyData()
    {
        const string json = """{"data":[]}""";

        var response = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterModelsResponse);

        response.ShouldNotBeNull();
        response.Data.ShouldNotBeNull();
        response.Data.Count.ShouldBe(0);
    }

    [Test]
    public void OpenRouterModelPricing_Deserialization_NullValues()
    {
        const string json = """{"prompt": null, "completion": null}""";

        var pricing = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterModelPricing);

        pricing.ShouldNotBeNull();
        pricing.Prompt.ShouldBeNull();
        pricing.Completion.ShouldBeNull();
    }

    [Test]
    public void OpenRouterTopProvider_Deserialization_NullOptionalFields()
    {
        const string json = """{"is_moderated": false}""";

        var topProvider = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterTopProvider);

        topProvider.ShouldNotBeNull();
        topProvider.ContextLength.ShouldBeNull();
        topProvider.MaxCompletionTokens.ShouldBeNull();
        topProvider.IsModerated.ShouldBeFalse();
    }

    // ── Image Generation ──────────────────────────────────────────────────────

    [Test]
    public async Task ChatAsync_ParsesGeneratedImages_DataUrl()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-1",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Here is your image.",
                    "images": [{
                        "type": "image_url",
                        "image_url": { "url": "data:image/png;base64,iVBORw0KGgo=" }
                    }]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.Content.ShouldBe("Here is your image.");
        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages!.Count.ShouldBe(1);
        response.GeneratedImages[0].Base64Data.ShouldBe("iVBORw0KGgo=");
        response.GeneratedImages[0].MimeType.ShouldBe("image/png");
    }

    [Test]
    public async Task ChatAsync_ParsesGeneratedImages_JpegDataUrl()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-jpeg",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "A JPEG image.",
                    "images": [{
                        "type": "image_url",
                        "image_url": { "url": "data:image/jpeg;base64,/9j/4AAQ=" }
                    }]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages![0].Base64Data.ShouldBe("/9j/4AAQ=");
        response.GeneratedImages[0].MimeType.ShouldBe("image/jpeg");
    }

    [Test]
    public async Task ChatAsync_ParsesMultipleGeneratedImages()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-multi",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Two images.",
                    "images": [
                        { "type": "image_url", "image_url": { "url": "data:image/png;base64,AAAA" } },
                        { "type": "image_url", "image_url": { "url": "data:image/webp;base64,BBBB" } }
                    ]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages!.Count.ShouldBe(2);
        response.GeneratedImages[0].MimeType.ShouldBe("image/png");
        response.GeneratedImages[0].Base64Data.ShouldBe("AAAA");
        response.GeneratedImages[1].MimeType.ShouldBe("image/webp");
        response.GeneratedImages[1].Base64Data.ShouldBe("BBBB");
    }

    [Test]
    public async Task ChatAsync_NoImages_GeneratedImagesIsNull()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldBeNull();
    }

    [Test]
    public async Task ChatAsync_EmptyImagesArray_GeneratedImagesIsNull()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-empty",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "No images.",
                    "images": []
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldBeNull();
    }

    [Test]
    public async Task ChatAsync_PlainBase64_DefaultsToPng()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-plain",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Plain base64.",
                    "images": [{
                        "type": "image_url",
                        "image_url": { "url": "iVBORw0KGgoAAAANSUhEUg==" }
                    }]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages![0].MimeType.ShouldBe("image/png");
        response.GeneratedImages[0].Base64Data.ShouldBe("iVBORw0KGgoAAAANSUhEUg==");
    }

    [Test]
    public async Task ChatAsync_ImageWithEmptyUrl_Skipped()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-empty-url",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Bad image.",
                    "images": [
                        { "type": "image_url", "image_url": { "url": "" } },
                        { "type": "image_url", "image_url": { "url": "data:image/png;base64,GOOD" } }
                    ]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages!.Count.ShouldBe(1);
        response.GeneratedImages[0].Base64Data.ShouldBe("GOOD");
    }

    [Test]
    public async Task ChatAsync_ImageWithNullImageUrl_Skipped()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-null-url",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Null image_url.",
                    "images": [
                        { "type": "image_url" },
                        { "type": "image_url", "image_url": { "url": "data:image/png;base64,VALID" } }
                    ]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages!.Count.ShouldBe(1);
        response.GeneratedImages[0].Base64Data.ShouldBe("VALID");
    }

    [Test]
    public async Task ChatAsync_MalformedDataUrl_Skipped()
    {
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-bad-data",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Malformed.",
                    "images": [
                        { "type": "image_url", "image_url": { "url": "data:badformat" } },
                        { "type": "image_url", "image_url": { "url": "data:image/gif;base64,R0lGODlh" } }
                    ]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages!.Count.ShouldBe(1);
        response.GeneratedImages[0].MimeType.ShouldBe("image/gif");
        response.GeneratedImages[0].Base64Data.ShouldBe("R0lGODlh");
    }

    [Test]
    public async Task ChatAsync_DataUrl_SemicolonAfterComma_Skipped()
    {
        // When semicolonIdx > commaIdx (e.g. "data:image/png,abc;def") the data URL
        // structure is malformed and the image should be skipped gracefully.
        var handler = new MockHandler(CreateJsonResponse("""
        {
            "id": "gen-img-bad-order",
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Bad order.",
                    "images": [
                        { "type": "image_url", "image_url": { "url": "data:image/png,abc;base64,AAAA" } },
                        { "type": "image_url", "image_url": { "url": "data:image/gif;base64,R0lGODlh" } }
                    ]
                },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
        }
        """));
        var provider = CreateProvider(handler);

        var response = await provider.ChatAsync(MinimalChatRequest);

        response.GeneratedImages.ShouldNotBeNull();
        response.GeneratedImages!.Count.ShouldBe(1);
        response.GeneratedImages[0].MimeType.ShouldBe("image/gif");
        response.GeneratedImages[0].Base64Data.ShouldBe("R0lGODlh");
    }

    [Test]
    public async Task StreamAsync_YieldsImageGenerationChunk()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Here is your image."}}]}""",
            """{"choices":[{"delta":{"images":[{"type":"image_url","image_url":{"url":"data:image/png;base64,STREAM_IMG"}}]}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("Here is your image.");
        var imgChunk = chunks.OfType<ImageGenerationChunk>().Single();
        imgChunk.Image.Base64Data.ShouldBe("STREAM_IMG");
        imgChunk.Image.MimeType.ShouldBe("image/png");
    }

    [Test]
    public async Task StreamAsync_MultipleImageChunks()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"images":[{"type":"image_url","image_url":{"url":"data:image/png;base64,IMG1"}}]}}]}""",
            """{"choices":[{"delta":{"images":[{"type":"image_url","image_url":{"url":"data:image/jpeg;base64,IMG2"}}]}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var imgChunks = chunks.OfType<ImageGenerationChunk>().ToList();
        imgChunks.Count.ShouldBe(2);
        imgChunks[0].Image.MimeType.ShouldBe("image/png");
        imgChunks[0].Image.Base64Data.ShouldBe("IMG1");
        imgChunks[1].Image.MimeType.ShouldBe("image/jpeg");
        imgChunks[1].Image.Base64Data.ShouldBe("IMG2");
    }

    [Test]
    public async Task StreamAsync_ImageWithEmptyUrl_SkippedNoChunk()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"images":[{"type":"image_url","image_url":{"url":""}}]}}]}""",
            """{"choices":[{"delta":{"content":"Text only"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.OfType<ImageGenerationChunk>().ShouldBeEmpty();
        chunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("Text only");
    }

    [Test]
    public void GeneratedImageContent_Deserialization_RoundTrip()
    {
        const string json = """
        {
            "type": "image_url",
            "image_url": { "url": "data:image/png;base64,abc123" }
        }
        """;

        var content = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.GeneratedImageContent);

        content.ShouldNotBeNull();
        content.Type.ShouldBe("image_url");
        content.ImageUrl.ShouldNotBeNull();
        content.ImageUrl!.Url.ShouldBe("data:image/png;base64,abc123");

        // Verify serialization round-trip
        var serialized = JsonSerializer.Serialize(content, OpenRouterJsonContext.Default.GeneratedImageContent);
        var roundTripped = JsonSerializer.Deserialize(serialized, OpenRouterJsonContext.Default.GeneratedImageContent);
        roundTripped.ShouldNotBeNull();
        roundTripped.ImageUrl!.Url.ShouldBe("data:image/png;base64,abc123");
    }

    [Test]
    public void GeneratedImageContent_Deserialization_NullImageUrl()
    {
        const string json = """{"type": "image_url"}""";

        var content = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.GeneratedImageContent);

        content.ShouldNotBeNull();
        content.ImageUrl.ShouldBeNull();
    }

    [Test]
    public void Request_Serialization_IncludesModalities()
    {
        var request = new OpenRouterRequest
        {
            Model = "openai/dall-e-3",
            Messages = [new CompletionMessage { Role = "user", Content = "Draw a cat" }],
            Modalities = ["image", "text"]
        };

        var json = JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.OpenRouterRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("modalities").GetArrayLength().ShouldBe(2);
        root.GetProperty("modalities")[0].GetString().ShouldBe("image");
        root.GetProperty("modalities")[1].GetString().ShouldBe("text");
    }

    [Test]
    public void Request_Serialization_OmitsNullModalities()
    {
        var request = new OpenRouterRequest
        {
            Model = "openai/gpt-4o",
            Messages = [new CompletionMessage { Role = "user", Content = "Hi" }]
        };

        var json = JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.OpenRouterRequest);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("modalities", out _).ShouldBeFalse();
    }

    // ── File Attachment Support ────────────────────────────────────────────────

    [Test]
    public async Task ChatAsync_WithFiles_SendsFileContentParts()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var pdfBase64 = Convert.ToBase64String("PDF content"u8.ToArray());
        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    "Summarize this document",
                    Files: [FileAttachment.Create(pdfBase64, "application/pdf", "report.pdf")])
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        // Content should be an array (multimodal) not a string
        content.ValueKind.ShouldBe(JsonValueKind.Array);

        // First part: file
        var filePart = content[0];
        filePart.GetProperty("type").GetString().ShouldBe("file");
        filePart.GetProperty("file").GetProperty("filename").GetString().ShouldBe("report.pdf");
        filePart.GetProperty("file").GetProperty("file_data").GetString()
            .ShouldStartWith("data:application/pdf;base64,");

        // Second part: text
        var textPart = content[1];
        textPart.GetProperty("type").GetString().ShouldBe("text");
        textPart.GetProperty("text").GetString().ShouldBe("Summarize this document");
    }

    [Test]
    public async Task ChatAsync_WithImagesAndFiles_SendsBothContentParts()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var imgBase64 = Convert.ToBase64String("fake image"u8.ToArray());
        var pdfBase64 = Convert.ToBase64String("fake pdf"u8.ToArray());
        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    "Analyze these",
                    Images: [ImageAttachment.Create(imgBase64, "image/png")],
                    Files: [FileAttachment.Create(pdfBase64, "application/pdf", "doc.pdf")])
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        content.ValueKind.ShouldBe(JsonValueKind.Array);

        // Should have 3 parts: image, file, text
        content.GetArrayLength().ShouldBe(3);
        content[0].GetProperty("type").GetString().ShouldBe("image_url");
        content[1].GetProperty("type").GetString().ShouldBe("file");
        content[2].GetProperty("type").GetString().ShouldBe("text");
    }

    [Test]
    public async Task ChatAsync_WithFilesOnly_NoImages_SendsFileContentParts()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var csvBase64 = Convert.ToBase64String("a,b,c\n1,2,3"u8.ToArray());
        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    "Parse this CSV",
                    Files: [FileAttachment.Create(csvBase64, "text/csv", "data.csv")])
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        content.ValueKind.ShouldBe(JsonValueKind.Array);

        content.GetArrayLength().ShouldBe(2); // file + text
        content[0].GetProperty("type").GetString().ShouldBe("file");
        content[0].GetProperty("file").GetProperty("filename").GetString().ShouldBe("data.csv");
        content[0].GetProperty("file").GetProperty("file_data").GetString()
            .ShouldStartWith("data:text/csv;base64,");
    }

    [Test]
    public async Task ChatAsync_MultipleFiles_AllIncludedInRequest()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var pdf1 = Convert.ToBase64String("pdf1"u8.ToArray());
        var pdf2 = Convert.ToBase64String("pdf2"u8.ToArray());
        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    "Compare these documents",
                    Files:
                    [
                        FileAttachment.Create(pdf1, "application/pdf", "doc1.pdf"),
                        FileAttachment.Create(pdf2, "application/pdf", "doc2.pdf")
                    ])
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        // 2 files + 1 text = 3 parts
        content.GetArrayLength().ShouldBe(3);
        content[0].GetProperty("file").GetProperty("filename").GetString().ShouldBe("doc1.pdf");
        content[1].GetProperty("file").GetProperty("filename").GetString().ShouldBe("doc2.pdf");
    }

    [Test]
    public void FileContentData_Serialization_CorrectPropertyNames()
    {
        var data = new FileContentData
        {
            Filename = "report.pdf",
            FileData = "data:application/pdf;base64,AAAA"
        };

        var json = JsonSerializer.Serialize(data, OpenAiJsonContext.Default.FileContentData);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("filename").GetString().ShouldBe("report.pdf");
        root.GetProperty("file_data").GetString().ShouldBe("data:application/pdf;base64,AAAA");
    }

    [Test]
    public void FileContentData_Deserialization_RoundTrip()
    {
        const string json = """{"filename":"test.txt","file_data":"data:text/plain;base64,SGVsbG8="}""";

        var data = JsonSerializer.Deserialize(json, OpenAiJsonContext.Default.FileContentData);

        data.ShouldNotBeNull();
        data.Filename.ShouldBe("test.txt");
        data.FileData.ShouldBe("data:text/plain;base64,SGVsbG8=");
    }

    [Test]
    public void ContentPart_WithFileType_Serialization()
    {
        var part = new ContentPart
        {
            Type = "file",
            File = new FileContentData
            {
                Filename = "doc.pdf",
                FileData = "data:application/pdf;base64,AAAA"
            }
        };

        var json = JsonSerializer.Serialize(part, OpenAiJsonContext.Default.ContentPart);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().ShouldBe("file");
        root.GetProperty("file").GetProperty("filename").GetString().ShouldBe("doc.pdf");
        root.GetProperty("file").GetProperty("file_data").GetString()
            .ShouldBe("data:application/pdf;base64,AAAA");

        // image_url and text should be omitted (null)
        root.TryGetProperty("image_url", out _).ShouldBeFalse();
        root.TryGetProperty("text", out _).ShouldBeFalse();
    }

    [Test]
    public void ContentPart_WithFileType_Deserialization()
    {
        const string json = """
        {
            "type": "file",
            "file": {
                "filename": "notes.md",
                "file_data": "data:text/markdown;base64,IyBIZWxsbw=="
            }
        }
        """;

        var part = JsonSerializer.Deserialize(json, OpenAiJsonContext.Default.ContentPart);

        part.ShouldNotBeNull();
        part.Type.ShouldBe("file");
        part.File.ShouldNotBeNull();
        part.File!.Filename.ShouldBe("notes.md");
        part.File.FileData.ShouldBe("data:text/markdown;base64,IyBIZWxsbw==");
        part.ImageUrl.ShouldBeNull();
        part.Text.ShouldBeNull();
    }

    [Test]
    public async Task ChatAsync_NoFilesNoImages_ContentIsString()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var request = new ChatRequest(
            Model: "openai/gpt-4o",
            Messages: [new ChatMessage(MessageRole.User, "Hello")]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        // When no files or images, content should be a plain string
        content.ValueKind.ShouldBe(JsonValueKind.String);
        content.GetString().ShouldBe("Hello");
    }

    // ── Audio Support ──────────────────────────────────────────────────────────

    [Test]
    public async Task ChatAsync_WithAudio_SendsInputAudioContentPart()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var audio = AudioAttachment.FromBase64("AAAA", "wav");
        var request = new ChatRequest(
            Model: "openai/gpt-4o-audio-preview",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    "What is this audio?",
                    Audio: audio)
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        // Content should be an array (multimodal) not a string
        content.ValueKind.ShouldBe(JsonValueKind.Array);

        // Should have 2 parts: input_audio + text
        content.GetArrayLength().ShouldBe(2);
        var audioPart = content[0];
        audioPart.GetProperty("type").GetString().ShouldBe("input_audio");
        audioPart.GetProperty("input_audio").GetProperty("data").GetString().ShouldBe("AAAA");
        audioPart.GetProperty("input_audio").GetProperty("format").GetString().ShouldBe("wav");

        var textPart = content[1];
        textPart.GetProperty("type").GetString().ShouldBe("text");
        textPart.GetProperty("text").GetString().ShouldBe("What is this audio?");
    }

    [Test]
    public async Task ChatAsync_WithImagesFilesAndAudio_SendsAllContentParts()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var imgBase64 = Convert.ToBase64String("fake image"u8.ToArray());
        var pdfBase64 = Convert.ToBase64String("fake pdf"u8.ToArray());
        var audio = AudioAttachment.FromBase64("audiodata", "mp3");

        var request = new ChatRequest(
            Model: "openai/gpt-4o-audio-preview",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    "Analyze all of these",
                    Images: [ImageAttachment.Create(imgBase64, "image/png")],
                    Files: [FileAttachment.Create(pdfBase64, "application/pdf", "doc.pdf")],
                    Audio: audio)
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        content.ValueKind.ShouldBe(JsonValueKind.Array);

        // Should have 4 parts: image, file, audio, text
        content.GetArrayLength().ShouldBe(4);
        content[0].GetProperty("type").GetString().ShouldBe("image_url");
        content[1].GetProperty("type").GetString().ShouldBe("file");
        content[2].GetProperty("type").GetString().ShouldBe("input_audio");
        content[3].GetProperty("type").GetString().ShouldBe("text");
    }

    [Test]
    public async Task ChatAsync_WithAudioOnly_NoText_SendsAudioPartOnly()
    {
        var handler = new MockHandler(CreateJsonResponse(MinimalResponse));
        var provider = CreateProvider(handler);

        var audio = AudioAttachment.FromBase64("BBBB", "ogg");
        var request = new ChatRequest(
            Model: "openai/gpt-4o-audio-preview",
            Messages:
            [
                new ChatMessage(
                    MessageRole.User,
                    null,
                    Audio: audio)
            ]
        );

        await provider.ChatAsync(request);

        handler.CapturedBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        content.ValueKind.ShouldBe(JsonValueKind.Array);

        // Only audio part (text is null/empty so no text part)
        content.GetArrayLength().ShouldBe(1);
        content[0].GetProperty("type").GetString().ShouldBe("input_audio");
    }

    [Test]
    public void AudioContentData_Serialization_RoundTrip()
    {
        var data = new AudioContentData { Data = "SGVsbG8=", Format = "wav" };

        var json = JsonSerializer.Serialize(data, OpenRouterJsonContext.Default.AudioContentData);
        var deserialized = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.AudioContentData);

        deserialized.ShouldNotBeNull();
        deserialized.Data.ShouldBe("SGVsbG8=");
        deserialized.Format.ShouldBe("wav");
    }

    [Test]
    public void StreamAudioDelta_Deserialization()
    {
        const string json = """{"data": "AAAA", "transcript": "Hello world"}""";

        var delta = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.StreamAudioDelta);

        delta.ShouldNotBeNull();
        delta.Data.ShouldBe("AAAA");
        delta.Transcript.ShouldBe("Hello world");
    }

    [Test]
    public void StreamAudioDelta_Deserialization_NullFields()
    {
        const string json = """{}""";

        var delta = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.StreamAudioDelta);

        delta.ShouldNotBeNull();
        delta.Data.ShouldBeNull();
        delta.Transcript.ShouldBeNull();
    }

    [Test]
    public async Task StreamAsync_AudioDelta_YieldsAudioDeltaChunks()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Here is your audio."}}]}""",
            """{"choices":[{"delta":{"audio":{"data":"CHUNK1","transcript":"Hello"}}}]}""",
            """{"choices":[{"delta":{"audio":{"data":"CHUNK2","transcript":" world"}}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("Here is your audio.");

        var audioChunks = chunks.OfType<AudioDeltaChunk>().ToList();
        audioChunks.Count.ShouldBe(2);
        audioChunks[0].AudioData.ShouldBe("CHUNK1");
        audioChunks[0].Transcript.ShouldBe("Hello");
        audioChunks[1].AudioData.ShouldBe("CHUNK2");
        audioChunks[1].Transcript.ShouldBe(" world");
    }

    [Test]
    public async Task StreamAsync_AudioDelta_TranscriptOnly()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"audio":{"transcript":"Just text"}}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        var audioChunk = chunks.OfType<AudioDeltaChunk>().Single();
        audioChunk.AudioData.ShouldBeNull();
        audioChunk.Transcript.ShouldBe("Just text");
    }

    [Test]
    public async Task StreamAsync_AudioDelta_EmptyFields_NoChunk()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"audio":{"data":"","transcript":""}}}]}""",
            """{"choices":[{"delta":{"content":"Text only"}}]}""",
            "[DONE]"
        );
        var handler = new MockHandler(CreateSseResponse(sse));
        var provider = CreateProvider(handler);

        var chunks = await CollectStreamChunks(provider);

        chunks.OfType<AudioDeltaChunk>().ShouldBeEmpty();
        chunks.OfType<TextDeltaChunk>().Single().Delta.ShouldBe("Text only");
    }

    [Test]
    public void OpenRouterAudioConfig_Serialization_RoundTrip()
    {
        var config = new OpenRouterAudioConfig { Voice = "nova", Format = "mp3" };

        var json = JsonSerializer.Serialize(config, OpenRouterJsonContext.Default.OpenRouterAudioConfig);
        var deserialized = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.OpenRouterAudioConfig);

        deserialized.ShouldNotBeNull();
        deserialized.Voice.ShouldBe("nova");
        deserialized.Format.ShouldBe("mp3");
    }

    [Test]
    public void OpenRouterAudioConfig_DefaultValues()
    {
        var config = new OpenRouterAudioConfig();

        config.Voice.ShouldBe("alloy");
        config.Format.ShouldBe("wav");
    }

    [Test]
    public void Request_Serialization_IncludesAudioConfig()
    {
        var request = new OpenRouterRequest
        {
            Model = "openai/gpt-4o-audio-preview",
            Messages = [new CompletionMessage { Role = "user", Content = "Hello" }],
            Modalities = ["text", "audio"],
            AudioConfig = new OpenRouterAudioConfig { Voice = "echo", Format = "opus" }
        };

        var json = JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.OpenRouterRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("modalities").GetArrayLength().ShouldBe(2);
        root.GetProperty("modalities")[0].GetString().ShouldBe("text");
        root.GetProperty("modalities")[1].GetString().ShouldBe("audio");
        root.GetProperty("audio").GetProperty("voice").GetString().ShouldBe("echo");
        root.GetProperty("audio").GetProperty("format").GetString().ShouldBe("opus");
    }

    [Test]
    public void Request_Serialization_OmitsNullAudioConfig()
    {
        var request = new OpenRouterRequest
        {
            Model = "openai/gpt-4o",
            Messages = [new CompletionMessage { Role = "user", Content = "Hi" }]
        };

        var json = JsonSerializer.Serialize(request, OpenRouterJsonContext.Default.OpenRouterRequest);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("audio", out _).ShouldBeFalse();
    }

    [Test]
    public void ContentPart_WithInputAudio_Serialization()
    {
        var part = new ContentPart
        {
            Type = "input_audio",
            InputAudio = new AudioContentData { Data = "base64data", Format = "wav" }
        };

        var json = JsonSerializer.Serialize(part, OpenRouterJsonContext.Default.ContentPart);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().ShouldBe("input_audio");
        root.GetProperty("input_audio").GetProperty("data").GetString().ShouldBe("base64data");
        root.GetProperty("input_audio").GetProperty("format").GetString().ShouldBe("wav");
        root.TryGetProperty("text", out _).ShouldBeFalse();
        root.TryGetProperty("image_url", out _).ShouldBeFalse();
        root.TryGetProperty("file", out _).ShouldBeFalse();
    }

    [Test]
    public void ContentPart_Deserialization_InputAudio()
    {
        const string json = """
        {
            "type": "input_audio",
            "input_audio": { "data": "AAAA", "format": "ogg" }
        }
        """;

        var part = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.ContentPart);

        part.ShouldNotBeNull();
        part.Type.ShouldBe("input_audio");
        part.InputAudio.ShouldNotBeNull();
        part.InputAudio!.Data.ShouldBe("AAAA");
        part.InputAudio.Format.ShouldBe("ogg");
        part.Text.ShouldBeNull();
        part.ImageUrl.ShouldBeNull();
        part.File.ShouldBeNull();
    }

    [Test]
    public void StreamDelta_WithAudio_Deserialization()
    {
        const string json = """
        {
            "content": null,
            "audio": { "data": "audiobase64", "transcript": "Hi there" }
        }
        """;

        var delta = JsonSerializer.Deserialize(json, OpenRouterJsonContext.Default.StreamDelta);

        delta.ShouldNotBeNull();
        delta.Content.ShouldBeNull();
        delta.Audio.ShouldNotBeNull();
        delta.Audio!.Data.ShouldBe("audiobase64");
        delta.Audio.Transcript.ShouldBe("Hi there");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private const string MinimalResponse = """
    {
        "id": "gen-0",
        "choices": [{ "message": { "role": "assistant", "content": "OK" }, "finish_reason": "stop" }],
        "usage": { "prompt_tokens": 5, "completion_tokens": 1, "total_tokens": 6 }
    }
    """;

    private static readonly ChatRequest MinimalChatRequest = new(
        Model: "openai/gpt-4o-mini",
        Messages: [new ChatMessage(MessageRole.User, "Hi")]
    );

    private static OpenRouterProvider CreateProvider(
        MockHandler handler,
        string apiKey = "test-key",
        string? httpReferer = null,
        string? appTitle = null,
        Dictionary<string, string>? extraHeaders = null,
        List<string>? apiKeys = null)
    {
        var factory = new SingleHandlerHttpClientFactory(handler);
        return new OpenRouterProvider(factory, apiKey, "openrouter", httpReferer, appTitle, extraHeaders, apiKeys);
    }

    private static HttpResponseMessage CreateJsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage CreateSseResponse(string sseData) =>
        new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sseData)))
        };

    private static string BuildSse(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (var line in dataLines)
        {
            sb.AppendLine($"data: {line}");
            sb.AppendLine(); // blank line = SSE event delimiter
        }
        return sb.ToString();
    }

    private static async Task<List<StreamChunk>> CollectStreamChunks(OpenRouterProvider provider)
    {
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(MinimalChatRequest))
        {
            chunks.Add(chunk);
        }
        return chunks;
    }

    /// <summary>
    /// Mock HTTP handler that captures request details and returns a canned response.
    /// </summary>
    private sealed class MockHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequest = request;
            if (request.Content is not null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(ct);
            }
            return response;
        }
    }

    /// <summary>
    /// HTTP handler that creates a fresh response for each call so the response content
    /// is not disposed between sequential requests. Captures all requests for assertions.
    /// </summary>
    private sealed class FreshResponseHandler(string jsonBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> CapturedRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
