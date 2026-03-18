using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Providers.Gemini;

public sealed class GeminiProvider(IHttpClientFactory httpClientFactory, string apiKey, string name = "gemini")
    : IProvider, IStreamingProvider, IHealthCheckableProvider
{
    private const string BaseUrl = ClawsharpConstants.GeminiDefaultBaseUrl + "/models";

    public string Name { get; } = name;

    /// <inheritdoc />
    public bool SupportsVision => true;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{request.Model}:generateContent";
        var gemReq = BuildGeminiRequest(request, url);

        var gemResp = await ProviderRequestHandler.ExecuteAsync(
                          httpClientFactory, gemReq, ConfigureHeaders, "Gemini API", ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Empty response from Gemini API.");

        if (gemResp.Error is { } err)
        {
            throw new HttpRequestException($"Gemini API error {err.Code}: {ProviderRequestHandler.SanitizeErrorBody(err.Message)}");
        }

        var candidate = gemResp.Candidates.FirstOrDefault()
                        ?? throw new InvalidOperationException("No candidates in Gemini response.");

        var textParts = new List<string>();
        var toolCalls = new List<ToolCall>();

        foreach (var part in candidate.Content.Parts)
        {
            if (part.Text is not null)
            {
                textParts.Add(part.Text);
            }

            if (part.FunctionCall is not null)
            {
                var id = $"call_{Guid.NewGuid():N}";
                toolCalls.Add(new ToolCall(id, part.FunctionCall.Name, part.FunctionCall.Args.GetRawText()));
            }
        }

        var finishReason = MapFinishReason(candidate.FinishReason);

        int? cacheRead = null;
        if (gemResp.UsageMetadata?.CachedContentTokenCount is > 0)
        {
            cacheRead = gemResp.UsageMetadata.CachedContentTokenCount;
        }

        string? textContent = null;
        if (textParts.Count > 0)
        {
            textContent = string.Join("\n", textParts);
        }

        List<ToolCall>? finalToolCalls = null;
        if (toolCalls.Count > 0)
        {
            finalToolCalls = toolCalls;
        }

        return new ChatResponse(
            textContent,
            finalToolCalls,
            finishReason,
            gemResp.UsageMetadata?.PromptTokenCount,
            gemResp.UsageMetadata?.CandidatesTokenCount,
            CacheReadTokens: cacheRead
        );
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{request.Model}:streamGenerateContent?alt=sse";
        var gemReq = BuildGeminiRequest(request, url);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(gemReq, GeminiJsonContext.Default.GenerateContentRequest);

        var (http, resp, body) = await ProviderRequestHandler.SendStreamingAsync(
            httpClientFactory, url, jsonBytes, ConfigureHeaders, "Gemini streaming API", ct).ConfigureAwait(false);

        using var _ = http;
        using var __ = resp;
        await using var stream = body;

        var doneEmitted = false;

        await foreach (var (_, data) in SseLineReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            GenerateContentResponse? gemResp;
            try
            {
                gemResp = JsonSerializer.Deserialize(data, GeminiJsonContext.Default.GenerateContentResponse);
            }
            catch (JsonException)
            {
                continue;
            }

            if (gemResp is null)
            {
                continue;
            }

            // MED-57: Check for error field in streaming response chunks.
            if (gemResp.Error is { } streamErr)
            {
                // Emit a done chunk before throwing so the stream is properly terminated.
                doneEmitted = true;
                yield return new StreamDoneChunk();
                throw new HttpRequestException(
                    $"Gemini streaming error {streamErr.Code}: {ProviderRequestHandler.SanitizeErrorBody(streamErr.Message)}");
            }

            if (gemResp.UsageMetadata is { } usageMeta)
            {
                yield return new StreamUsageChunk(
                    InputTokens: usageMeta.PromptTokenCount,
                    OutputTokens: usageMeta.CandidatesTokenCount,
                    CacheReadTokens: usageMeta.CachedContentTokenCount,
                    CacheWriteTokens: 0);
            }

            var candidate = gemResp.Candidates.FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            var toolIndex = 0;
            foreach (var part in candidate.Content.Parts)
            {
                if (part.Text is not null)
                {
                    yield return new TextDeltaChunk(part.Text);
                }

                if (part.FunctionCall is not null)
                {
                    var id = $"call_{Guid.NewGuid():N}";
                    yield return new ToolCallChunk(toolIndex++, id, part.FunctionCall.Name, part.FunctionCall.Args.GetRawText());
                }
            }
        }

        // MED-55: Ensure a done chunk is always emitted, even if the stream ended without
        // an explicit terminal event (e.g. network interruption or early server close).
        if (!doneEmitted)
        {
            yield return new StreamDoneChunk();
        }
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var http = httpClientFactory.CreateClient("llm");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}?key={apiKey}");

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

    private static GenerateContentRequest BuildGeminiRequest(ChatRequest request, string url)
    {
        ContentItem? systemInstruction = null;
        var contents = new List<ContentItem>();

        foreach (var msg in request.Messages)
        {
            if (msg.Role == MessageRole.System)
            {
                systemInstruction = new ContentItem
                {
                    Parts = [new ContentPart { Text = msg.Content }]
                };
                continue;
            }

            if (msg.Role == MessageRole.Tool)
            {
                var toolResponseJson = $"{{\"output\":{JsonSerializer.Serialize(msg.Content ?? "", GeminiJsonContext.Default.String)}}}";
                using var respDoc = JsonDocument.Parse(toolResponseJson);
                contents.Add(new ContentItem
                {
                    Role = "user",
                    Parts =
                    [
                        new ContentPart
                        {
                            FunctionResponse = new FunctionResponse
                            {
                                Name = msg.Name ?? "tool",
                                Response = respDoc.RootElement.Clone()
                            }
                        }
                    ]
                });
                continue;
            }

            if (msg.ToolCalls?.Count > 0)
            {
                var parts = new List<ContentPart>();
                if (msg.Content is not null)
                {
                    parts.Add(new ContentPart { Text = msg.Content });
                }

                foreach (var tc in msg.ToolCalls)
                {
                    using var argsDoc = JsonDocument.Parse(tc.ArgumentsJson);
                    parts.Add(new ContentPart
                    {
                        FunctionCall = new FunctionCall
                        {
                            Name = tc.Name,
                            Args = argsDoc.RootElement.Clone()
                        }
                    });
                }

                contents.Add(new ContentItem { Role = "model", Parts = parts });
                continue;
            }

            var role = msg.Role == MessageRole.Assistant ? "model" : "user";

            // Build parts: text + optional image attachments for multimodal messages.
            var msgParts = new List<ContentPart>();
            if (msg.Images is { Count: > 0 } images)
            {
                foreach (var img in images)
                {
                    msgParts.Add(new ContentPart
                    {
                        InlineData = new GeminiInlineData
                        {
                            MimeType = img.MimeType,
                            Data = img.Base64Data
                        }
                    });
                }
            }

            if (msg.Content is { Length: > 0 } contentText)
            {
                msgParts.Add(new ContentPart { Text = contentText });
            }
            else if (msgParts.Count == 0)
            {
                // Ensure at least one part even for empty content.
                msgParts.Add(new ContentPart { Text = "" });
            }

            contents.Add(new ContentItem
            {
                Role = role,
                Parts = msgParts
            });
        }

        List<ToolDefinition>? gemTools = null;
        if (request.Tools?.Count > 0)
        {
            gemTools =
            [
                new ToolDefinition
                {
                    FunctionDeclarations = request.Tools.Select(t =>
                    {
                        using var paramDoc = JsonDocument.Parse(t.ParametersSchemaJson);
                        return new FunctionDeclaration
                        {
                            Name = t.Name,
                            Description = t.Description,
                            Parameters = paramDoc.RootElement.Clone()
                        };
                    }).ToList()
                }
            ];
        }

        GeminiThinkingConfig? thinkingConfig = null;
        if (request.GeminiThinkingBudget > 0)
        {
            thinkingConfig = new GeminiThinkingConfig { ThinkingBudget = request.GeminiThinkingBudget };
        }

        return new GenerateContentRequest
        {
            Contents = contents,
            SystemInstruction = systemInstruction,
            Tools = gemTools,
            GenerationConfig = new GenerationConfig
            {
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxTokens
            },
            ThinkingConfig = thinkingConfig,
            Url = url
        };
    }

    /// <summary>
    /// Adds the Gemini API key header (<c>x-goog-api-key</c>) to an outgoing HTTP request.
    /// </summary>
    private void ConfigureHeaders(HttpRequestMessage req)
    {
        req.Headers.Add("x-goog-api-key", apiKey);
    }

    private static FinishReason MapFinishReason(string? geminiReason) => geminiReason switch
    {
        "STOP" => FinishReason.Stop,
        "MAX_TOKENS" => FinishReason.Length,
        "SAFETY" or "RECITATION" or "LANGUAGE" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII"
            => FinishReason.ContentFilter,
        _ => FinishReason.Stop
    };
}