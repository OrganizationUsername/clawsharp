using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.OpenRouter;

/// <summary>
/// Dedicated provider for OpenRouter (openrouter.ai). OpenRouter's API is a superset of the
/// OpenAI chat completions format with additional fields for model routing, provider preferences,
/// cost reporting, and plugins.
/// </summary>
public sealed class OpenRouterProvider(
    IHttpClientFactory httpClientFactory,
    string apiKey,
    string name = "openrouter",
    string? httpReferer = null,
    string? appTitle = null,
    Dictionary<string, string>? extraHeaders = null,
    List<string>? apiKeys = null)
    : IProvider, IStreamingProvider, IHealthCheckableProvider
{
    private const string BaseUrl = "https://openrouter.ai/api/v1";
    private const string ChatCompletionsUrl = $"{BaseUrl}/chat/completions";
    private const string ErrorFinishReason = "error";
    private const string DefaultImageMimeType = "image/png";

    private readonly ApiKeyRotator _keyRotator = new(apiKey, apiKeys);

    public string Name { get; } = name;

    /// <inheritdoc />
    public bool SupportsVision => true;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var orReq = BuildRequest(request, false, ChatCompletionsUrl);

        var orResp = await ProviderRequestHandler.ExecuteAsync(
                         httpClientFactory, orReq, ConfigureHeaders, "OpenRouter API", ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Empty response from OpenRouter API.");

        if (orResp.Choices.Count == 0)
            throw new InvalidOperationException("No choices in OpenRouter response.");
        var choice = orResp.Choices[0];

        // OpenRouter may return per-choice errors (e.g. moderation, provider disconnect)
        if (choice.FinishReason == ErrorFinishReason || choice.Error is not null)
        {
            var errorMsg = choice.Error?.Message ?? "Unknown provider error";
            throw new InvalidOperationException($"OpenRouter provider error: {errorMsg}");
        }

        List<ToolCall>? toolCalls = null;
        if (choice.Message.ToolCalls is { Count: > 0 } rawToolCalls)
        {
            toolCalls = new List<ToolCall>(rawToolCalls.Count);
            foreach (var tc in rawToolCalls)
            {
                toolCalls.Add(new ToolCall(tc.Id, tc.Function.Name, tc.Function.Arguments));
            }
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

        // Parse generated images from response
        IReadOnlyList<ImageAttachment>? generatedImages = null;
        if (choice.Message.Images is { Count: > 0 } imageOutputs)
        {
            generatedImages = ParseGeneratedImages(imageOutputs);
        }

        return new ChatResponse(
            content,
            toolCalls,
            finishReason,
            orResp.Usage?.PromptTokens,
            orResp.Usage?.CompletionTokens,
            choice.Message.ReasoningContent,
            CacheReadTokens: orResp.Usage?.PromptDetails?.CachedTokens,
            CacheWriteTokens: orResp.Usage?.PromptDetails?.CacheWriteTokens,
            GeneratedImages: generatedImages,
            ProviderReportedCost: orResp.Usage?.Cost
        );
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var orReq = BuildRequest(request, true);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(orReq, OpenRouterJsonContext.Default.OpenRouterRequest);
        var thinkFilter = TagStripFilter.CreateThinkingFilter();
        var toolTagFilter = TagStripFilter.CreateStreamingFilter();

        var (http, resp, body) = await ProviderRequestHandler.SendStreamingAsync(
            httpClientFactory, ChatCompletionsUrl, jsonBytes, ConfigureHeaders, "OpenRouter API stream", ct).ConfigureAwait(false);

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

            OpenRouterStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(data, OpenRouterJsonContext.Default.OpenRouterStreamChunk);
            }
            catch (JsonException)
            {
                continue; // Skip malformed lines
            }

            // Mid-stream error: OpenRouter sends a chunk with a top-level error object
            // and finish_reason: "error" after streaming has started (HTTP 200 already sent).
            if (chunk?.Error is { } midStreamError)
            {
                throw new InvalidOperationException(
                    $"OpenRouter mid-stream error ({midStreamError.Code}): {midStreamError.Message}");
            }

            if (chunk?.Usage is { } usage)
            {
                yield return new StreamUsageChunk(
                    InputTokens: usage.PromptTokens,
                    OutputTokens: usage.CompletionTokens,
                    CacheReadTokens: usage.PromptDetails?.CachedTokens ?? 0,
                    CacheWriteTokens: usage.PromptDetails?.CacheWriteTokens ?? 0,
                    ProviderReportedCost: usage.Cost);
            }

            if (chunk?.Choices is not { Count: > 0 } choices)
            {
                continue;
            }

            // Check for finish_reason: "error" even without a top-level error object
            if (choices[0].FinishReason == ErrorFinishReason)
            {
                throw new InvalidOperationException("OpenRouter stream terminated with error finish reason.");
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

            if (delta.Audio is { } audioDelta && (audioDelta.Data is { Length: > 0 } || audioDelta.Transcript is { Length: > 0 }))
            {
                yield return new AudioDeltaChunk(audioDelta.Data, audioDelta.Transcript);
            }

            if (delta.Images is { Count: > 0 } streamImages)
            {
                foreach (var img in ParseGeneratedImages(streamImages))
                {
                    yield return new ImageGenerationChunk(img);
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

    /// <summary>
    /// Queries the OpenRouter API key endpoint for credit balance and usage stats.
    /// Returns null if the request fails.
    /// </summary>
    internal async Task<OpenRouterKeyInfo?> GetKeyInfoAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = httpClientFactory.CreateClient("llm");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/key");
            ConfigureHeaders(req);

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(body, OpenRouterJsonContext.Default.OpenRouterKeyInfoResponse);
            return result?.Data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the full model list from OpenRouter's /api/v1/models endpoint.
    /// Returns an empty list if the request fails.
    /// </summary>
    internal async Task<IReadOnlyList<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = httpClientFactory.CreateClient("llm");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
            ConfigureHeaders(req);

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return [];

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(body, OpenRouterJsonContext.Default.OpenRouterModelsResponse);
            return result?.Data ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Resolves the context window size for a specific model by querying the OpenRouter models API.
    /// Returns null if the model is not found or the request fails.
    /// </summary>
    internal async Task<int?> GetModelContextLengthAsync(string modelId, CancellationToken ct = default)
    {
        var models = await ListModelsAsync(ct).ConfigureAwait(false);
        var model = models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        return model?.ContextLength;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var http = httpClientFactory.CreateClient("llm");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/key");
            ConfigureHeaders(req);

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var creditsMsg = TryExtractCreditsMessage(body);
                return new HealthCheckResult(true, creditsMsg ?? $"HTTP {(int)resp.StatusCode}", sw.Elapsed);
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
    /// Attempts to parse the credit balance from an OpenRouter key info response body.
    /// Returns a human-readable message on success, or null if parsing fails.
    /// </summary>
    private static string? TryExtractCreditsMessage(string body)
    {
        try
        {
            var result = JsonSerializer.Deserialize(body, OpenRouterJsonContext.Default.OpenRouterKeyInfoResponse);
            if (result?.Data?.LimitRemaining is { } remaining)
            {
                return $"OK — ${remaining:F2} credits remaining";
            }
        }
        catch
        {
            // Fall through
        }

        return null;
    }

    /// <summary>
    /// Parses generated image content parts into ImageAttachment objects.
    /// Handles both data URLs (data:image/png;base64,...) and plain base64.
    /// </summary>
    private static List<ImageAttachment> ParseGeneratedImages(List<GeneratedImageContent> images)
    {
        var result = new List<ImageAttachment>(images.Count);
        foreach (var img in images)
        {
            var url = img.ImageUrl?.Url;
            if (string.IsNullOrEmpty(url)) continue;

            string base64;
            string mime;

            if (url.StartsWith("data:", StringComparison.Ordinal))
            {
                // Parse data URL: data:image/png;base64,iVBOR...
                var semicolonIdx = url.IndexOf(';');
                var commaIdx = url.IndexOf(',');
                if (semicolonIdx < 0 || commaIdx < 0 || semicolonIdx > commaIdx) continue;

                mime = url[5..semicolonIdx]; // "image/png"
                base64 = url[(commaIdx + 1)..];
            }
            else
            {
                // Plain base64 with no data URL prefix — assume PNG
                base64 = url;
                mime = DefaultImageMimeType;
            }

            result.Add(new ImageAttachment { Base64Data = base64, MimeType = mime });
        }

        return result;
    }

    /// <summary>
    /// Adds authentication and OpenRouter-specific headers to an outgoing HTTP request.
    /// Always sets <c>Authorization: Bearer</c>. Optionally sets <c>HTTP-Referer</c> and
    /// <c>X-Title</c> for OpenRouter analytics. Applies extra headers (blocking auth duplication).
    /// </summary>
    private void ConfigureHeaders(HttpRequestMessage req)
    {
        var effectiveKey = _keyRotator.GetNext();

        if (!string.IsNullOrEmpty(effectiveKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveKey);
        }

        if (httpReferer is not null)
        {
            req.Headers.TryAddWithoutValidation("HTTP-Referer", httpReferer);
        }

        if (appTitle is not null)
        {
            req.Headers.TryAddWithoutValidation("X-Title", appTitle);
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
    /// Builds an OpenRouter chat completion request from the internal <see cref="ChatRequest"/>.
    /// Same message/tool conversion logic as <see cref="OpenAiProvider"/> but produces an
    /// <see cref="OpenRouterRequest"/> with OpenRouter-specific fields available for extension.
    /// </summary>
    private static OpenRouterRequest BuildRequest(ChatRequest request, bool stream, string url = "")
    {
        var messages = new List<CompletionMessage>(request.Messages.Count);
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
            if (m.Content is not null || m.Images is not null || m.Files is not null || m.Audio is not null || m.Videos is not null)
            {
                content = MessageContentBuilder.From(m).Build();
            }

            messages.Add(new CompletionMessage
            {
                Role = m.Role.Value,
                Content = content,
                ToolCallId = m.ToolCallId,
                Name = m.Name,
                ToolCalls = toolCalls
            });
        }

        List<FunctionTool>? tools = null;
        if (request.Tools is { Count: > 0 } toolDefs)
        {
            tools = new List<FunctionTool>(toolDefs.Count);
            foreach (var t in toolDefs)
            {
                using var paramDoc = JsonDocument.Parse(t.ParametersSchemaJson);
                tools.Add(new FunctionTool
                {
                    Function = new FunctionDefinition
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = paramDoc.RootElement.Clone()
                    }
                });
            }
        }

        StreamOptions? streamOptions = null;
        if (stream)
        {
            streamOptions = new StreamOptions { IncludeUsage = true };
        }

        string? toolChoice = null;
        if (tools is { Count: > 0 })
        {
            toolChoice = "auto";
        }

        return new OpenRouterRequest
        {
            Model = request.Model,
            Messages = messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Tools = tools,
            ToolChoice = toolChoice,
            ReasoningEffort = request.ReasoningEffort,
            Stream = stream,
            StreamOptions = streamOptions,
            Url = url
        };
    }
}
