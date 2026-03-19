using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Clawsharp.Core;

namespace Clawsharp.Providers.OpenAi;

public sealed class OpenAiProvider(
    IHttpClientFactory httpClientFactory,
    string baseUrl,
    string apiKey,
    string name = "openai",
    string? authHeader = null,
    Dictionary<string, string>? extraHeaders = null,
    List<string>? apiKeys = null)
    : IProvider, IStreamingProvider, IHealthCheckableProvider
{
    private readonly ApiKeyRotator _keyRotator = new(apiKey, apiKeys);
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public string Name { get; } = name;

    /// <inheritdoc />
    public bool SupportsVision => true;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/chat/completions";
        var oaiReq = BuildChatCompletionRequest(request, false, url);

        var oaiResp = await ProviderRequestHandler.ExecuteAsync(
                          httpClientFactory, oaiReq, ConfigureHeaders, "OpenAI API", ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Empty response from OpenAI API.");

        var choice = oaiResp.Choices.FirstOrDefault()
                     ?? throw new InvalidOperationException("No choices in OpenAI response.");

        List<ToolCall>? toolCalls = null;
        if (choice.Message.ToolCalls?.Count > 0)
        {
            toolCalls = choice.Message.ToolCalls.Select(tc => new ToolCall(
                tc.Id,
                tc.Function.Name,
                tc.Function.Arguments
            )).ToList();
        }

        var finishReason = choice.FinishReason switch
        {
            "stop" => FinishReason.Stop,
            "length" => FinishReason.Length,
            "tool_calls" or "function_call" => FinishReason.ToolCalls,
            "content_filter" => FinishReason.ContentFilter,
            _ => FinishReason.Stop
        };

        var content = choice.Message.Content?.Text;
        if (content is not null)
        {
            content = TagStripFilter.StripThinkingBlocks(content);
            content = TagStripFilter.StripToolTags(content);
        }

        return new ChatResponse(
            content,
            toolCalls,
            finishReason,
            oaiResp.Usage?.PromptTokens,
            oaiResp.Usage?.CompletionTokens,
            choice.Message.ReasoningContent,
            CacheReadTokens: oaiResp.Usage?.Details?.CachedTokens
        );
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var oaiReq = BuildChatCompletionRequest(request, true);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(oaiReq, OpenAiJsonContext.Default.ChatCompletionRequest);
        var url = $"{_baseUrl}/chat/completions";
        var thinkFilter = TagStripFilter.CreateThinkingFilter();
        var toolTagFilter = TagStripFilter.CreateStreamingFilter();

        var (http, resp, body) = await ProviderRequestHandler.SendStreamingAsync(
            httpClientFactory, url, jsonBytes, ConfigureHeaders, "OpenAI API stream", ct).ConfigureAwait(false);

        using var _ = http;
        using var __ = resp;
        await using var stream = body;

        await foreach (var (_, data) in SseLineReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            if (data == "[DONE]")
            {
                var remaining = thinkFilter.Flush();
                if (remaining.Length > 0)
                {
                    remaining = toolTagFilter.ProcessChunk(remaining);
                    if (remaining.Length > 0)
                    {
                        yield return new TextDeltaChunk(remaining);
                    }
                }

                var toolRemaining = toolTagFilter.Flush();
                if (toolRemaining.Length > 0)
                {
                    yield return new TextDeltaChunk(toolRemaining);
                }

                yield return new StreamDoneChunk();
                yield break;
            }

            CompletionStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(data, OpenAiJsonContext.Default.CompletionStreamChunk);
            }
            catch (JsonException)
            {
                continue; // Skip malformed lines
            }

            if (chunk?.Usage is { } usage)
            {
                yield return new StreamUsageChunk(
                    InputTokens: usage.PromptTokens,
                    OutputTokens: usage.CompletionTokens,
                    CacheReadTokens: usage.Details?.CachedTokens ?? 0,
                    CacheWriteTokens: 0);
            }

            if (chunk?.Choices is not { Count: > 0 } choices)
            {
                continue;
            }

            var delta = choices[0].Delta;
            if (delta is null)
            {
                continue;
            }

            if (delta.Content is { Length: > 0 } text)
            {
                var filtered = thinkFilter.ProcessChunk(text);
                if (filtered.Length > 0)
                {
                    filtered = toolTagFilter.ProcessChunk(filtered);
                    if (filtered.Length > 0)
                    {
                        yield return new TextDeltaChunk(filtered);
                    }
                }
            }

            if (delta.ReasoningContent is { Length: > 0 } reasoning)
            {
                yield return new ThinkingDeltaChunk(reasoning);
            }

            if (delta.ToolCalls is { Count: > 0 } toolCallDeltas)
            {
                foreach (var tc in toolCallDeltas)
                {
                    yield return new ToolCallChunk(
                        tc.Index,
                        tc.Id,
                        tc.Function?.Name,
                        tc.Function?.Arguments
                    );
                }
            }
        }

        var trailingText = thinkFilter.Flush();
        if (trailingText.Length > 0)
        {
            trailingText = toolTagFilter.ProcessChunk(trailingText);
            if (trailingText.Length > 0)
            {
                yield return new TextDeltaChunk(trailingText);
            }
        }

        var trailingToolText = toolTagFilter.Flush();
        if (trailingToolText.Length > 0)
        {
            yield return new TextDeltaChunk(trailingToolText);
        }

        yield return new StreamDoneChunk();
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var http = httpClientFactory.CreateClient("llm");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
            ConfigureHeaders(req);

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();

            if (resp.IsSuccessStatusCode)
            {
                return new HealthCheckResult(true, $"HTTP {(int)resp.StatusCode}", sw.Elapsed);
            }

            return new HealthCheckResult(false, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult(false, ex.Message, sw.Elapsed);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds authentication to an outgoing HTTP request. When <see cref="authHeader"/> is set,
    /// the API key is sent as a custom header (e.g. <c>api-key</c> for Azure OpenAI);
    /// otherwise falls back to standard <c>Authorization: Bearer</c>.
    /// Skipped entirely when the API key is empty (local providers like Ollama / LM Studio).
    /// When <see cref="apiKeys"/> is configured, rotates through keys using round-robin.
    /// </summary>
    private void ConfigureHeaders(HttpRequestMessage req)
    {
        var effectiveKey = _keyRotator.GetNext();

        if (!string.IsNullOrEmpty(effectiveKey))
        {
            if (authHeader is not null)
            {
                req.Headers.TryAddWithoutValidation(authHeader, effectiveKey);
            }
            else
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveKey);
            }
        }

        if (extraHeaders is not null)
        {
            foreach (var (headerName, headerValue) in extraHeaders)
            {
                if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(headerName, "x-api-key", StringComparison.OrdinalIgnoreCase))
                    continue;

                req.Headers.TryAddWithoutValidation(headerName, headerValue);
            }
        }
    }

    /// <summary>
    /// Sanitizes a raw API error body by truncating and stripping secret patterns.
    /// Delegates to <see cref="ProviderRequestHandler.SanitizeErrorBody"/> for consistent handling.
    /// Kept as a forwarding method for backward compatibility with callers that reference
    /// <c>OpenAiProvider.Sanitize</c> (e.g. Bedrock error paths).
    /// </summary>
    internal static string Sanitize(string raw) => ProviderRequestHandler.SanitizeErrorBody(raw);

    private static MessageContent BuildContent(string? text, IReadOnlyList<ImageAttachment>? images)
    {
        if (images is null || images.Count == 0)
        {
            return new MessageContent { Text = text };
        }

        var parts = new List<ContentPart>(images.Count + 1);
        foreach (var img in images)
        {
            parts.Add(new ContentPart
            {
                Type = "image_url",
                ImageUrl = new ImageUrl { Url = $"data:{img.MimeType};base64,{img.Base64Data}" }
            });
        }

        if (text is { Length: > 0 })
        {
            parts.Add(new ContentPart { Type = "text", Text = text });
        }

        return new MessageContent { Parts = parts };
    }

    /// <summary>
    /// Builds an OpenAI chat completion request from the internal <see cref="ChatRequest"/>.
    /// System prompt is provided via Messages[0] (role=system) by AgentLoop --
    /// SystemStaticPart/SystemDynamicPart are handled at the AgentLoop level and embedded into
    /// Messages before the provider is called. These split fields exist on ChatRequest solely
    /// for caching-aware providers (Anthropic) that need them as separate blocks.
    /// </summary>
    private static ChatCompletionRequest BuildChatCompletionRequest(ChatRequest request, bool stream, string url = "")
    {
        var oaiMessages = new List<CompletionMessage>(request.Messages.Count);
        foreach (var m in request.Messages)
        {
            List<FunctionToolCall>? toolCalls = null;
            if (m.ToolCalls is { Count: > 0 } tcs)
            {
                toolCalls = new List<FunctionToolCall>(tcs.Count);
                foreach (var tc in tcs)
                {
                    toolCalls.Add(new FunctionToolCall
                    {
                        Id = tc.Id,
                        Function = new FunctionToolCallDetail { Name = tc.Name, Arguments = tc.ArgumentsJson }
                    });
                }
            }

            MessageContent? content = null;
            if (m.Content is not null || m.Images is not null)
            {
                content = BuildContent(m.Content, m.Images);
            }

            oaiMessages.Add(new CompletionMessage
            {
                Role = m.Role.Value,
                Content = content,
                ToolCallId = m.ToolCallId,
                Name = m.Name,
                ToolCalls = toolCalls
            });
        }

        List<FunctionTool>? oaiTools = null;
        if (request.Tools?.Count > 0)
        {
            oaiTools = request.Tools.Select(t =>
            {
                using var paramDoc = JsonDocument.Parse(t.ParametersSchemaJson);
                return new FunctionTool
                {
                    Function = new FunctionDefinition
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = paramDoc.RootElement.Clone()
                    }
                };
            }).ToList();
        }

        StreamOptions? streamOptions = null;
        if (stream)
        {
            streamOptions = new StreamOptions { IncludeUsage = true };
        }

        string? toolChoice = null;
        if (oaiTools is { Count: > 0 })
        {
            toolChoice = "auto";
        }

        return new ChatCompletionRequest
        {
            Model = request.Model,
            Messages = oaiMessages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Tools = oaiTools,
            ToolChoice = toolChoice,
            ReasoningEffort = request.ReasoningEffort,
            Stream = stream,
            StreamOptions = streamOptions,
            Url = url
        };
    }
}