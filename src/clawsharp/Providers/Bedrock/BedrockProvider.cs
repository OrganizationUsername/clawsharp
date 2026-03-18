using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Clawsharp.Core;

namespace Clawsharp.Providers.Bedrock;

/// <summary>
///     AWS Bedrock provider using the Converse API with SigV4 request signing.
///     Implements <see cref="IStreamingProvider"/> for both non-streaming and streaming responses.
/// </summary>
public sealed class BedrockProvider(
    IHttpClientFactory httpClientFactory,
    string accessKeyId,
    string secretAccessKey,
    string region,
    string name = "bedrock")
    : IStreamingProvider
{
    private const string Service = "bedrock-runtime";

    private const int MaxErrorBodyBytes = 4096;

    public string Name { get; } = name;

    /// <inheritdoc />
    public bool SupportsVision => true;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var converseRequest = BuildRequest(request);
        var json = JsonSerializer.Serialize(converseRequest, BedrockJsonContext.Default.BedrockConverseRequest);

        // URL-encode the model ID (handles "/" in model ARNs and cross-region IDs)
        var encodedModel = Uri.EscapeDataString(request.Model);
        var endpoint = $"https://{Service}.{region}.amazonaws.com/model/{encodedModel}/converse";
        var uri = new Uri(endpoint);

        // Sign the request with SigV4
        var headers = AwsSigV4Signer.Sign("POST", uri, json, accessKeyId, secretAccessKey, region, Service, DateTimeOffset.UtcNow);

        using var http = httpClientFactory.CreateClient("llm");
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, uri);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

        foreach (var (key, value) in headers)
        {
            httpReq.Headers.TryAddWithoutValidation(key, value);
        }

        using var resp = await http.SendAsync(httpReq, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errBytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var limitedBytes = errBytes;
            if (errBytes.Length > MaxErrorBodyBytes)
            {
                limitedBytes = errBytes[..MaxErrorBodyBytes];
            }

            var err = Encoding.UTF8.GetString(limitedBytes);
            throw new HttpRequestException($"Bedrock Converse API error {resp.StatusCode}: {ProviderRequestHandler.SanitizeErrorBody(err)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var converseResponse = await JsonSerializer.DeserializeAsync(stream, BedrockJsonContext.Default.BedrockConverseResponse, ct)
                                                   .ConfigureAwait(false)
                               ?? throw new InvalidOperationException("Empty response from Bedrock Converse API.");

        return MapResponse(converseResponse);
    }

    // ── Streaming ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var converseRequest = BuildRequest(request);
        var json = JsonSerializer.Serialize(converseRequest, BedrockJsonContext.Default.BedrockConverseRequest);

        var encodedModel = Uri.EscapeDataString(request.Model);
        var endpoint = $"https://{Service}.{region}.amazonaws.com/model/{encodedModel}/converse-stream";
        var uri = new Uri(endpoint);

        var headers = AwsSigV4Signer.Sign("POST", uri, json, accessKeyId, secretAccessKey, region, Service, DateTimeOffset.UtcNow);

        using var http = httpClientFactory.CreateClient("llm");
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, uri);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

        foreach (var (key, value) in headers)
        {
            httpReq.Headers.TryAddWithoutValidation(key, value);
        }

        using var resp = await http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errBytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var limitedBytes = errBytes;
            if (errBytes.Length > MaxErrorBodyBytes)
            {
                limitedBytes = errBytes[..MaxErrorBodyBytes];
            }

            var err = Encoding.UTF8.GetString(limitedBytes);
            throw new HttpRequestException(
                $"Bedrock ConverseStream API error {resp.StatusCode}: {ProviderRequestHandler.SanitizeErrorBody(err)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var doneEmitted = false;
        await foreach (var (eventType, payload) in BedrockStreamParser.ParseAsync(stream, ct))
        {
            switch (eventType)
            {
                case "contentBlockStart":
                {
                    var evt = JsonSerializer.Deserialize(payload.Span, BedrockJsonContext.Default.BedrockStreamContentBlockStart);
                    if (evt?.Start?.ToolUse is { } toolStart)
                    {
                        yield return new ToolCallChunk(evt.ContentBlockIndex, toolStart.ToolUseId, toolStart.Name, null);
                    }

                    break;
                }
                case "contentBlockDelta":
                {
                    var evt = JsonSerializer.Deserialize(payload.Span, BedrockJsonContext.Default.BedrockStreamContentBlockDelta);
                    if (evt?.Delta?.Text is { } text)
                    {
                        yield return new TextDeltaChunk(text);
                    }
                    else if (evt?.Delta?.ToolUse is { Input: { } input })
                    {
                        yield return new ToolCallChunk(evt.ContentBlockIndex, null, null, input);
                    }

                    break;
                }
                case "metadata":
                {
                    var meta = JsonSerializer.Deserialize(payload.Span, BedrockJsonContext.Default.BedrockStreamMetadata);
                    if (meta?.Usage is { } usage)
                    {
                        yield return new StreamUsageChunk(
                            InputTokens: usage.InputTokens,
                            OutputTokens: usage.OutputTokens,
                            CacheReadTokens: 0,
                            CacheWriteTokens: 0);
                    }

                    break;
                }
                case "messageStop":
                {
                    doneEmitted = true;
                    yield return new StreamDoneChunk();
                    break;
                }
                default:
                {
                    // HIGH-11: Bedrock exception event types (e.g. throttlingException,
                    // modelStreamErrorException, validationException) were previously silently
                    // swallowed. Surface them as errors.
                    if (eventType.EndsWith("Exception", StringComparison.Ordinal) ||
                        eventType.Equals("error", StringComparison.Ordinal))
                    {
                        var errorPayload = Encoding.UTF8.GetString(payload.Span);
                        yield return new StreamDoneChunk();
                        throw new InvalidOperationException(
                            $"Bedrock stream exception '{eventType}': {ProviderRequestHandler.SanitizeErrorBody(errorPayload)}");
                    }

                    break;
                }
            }
        }

        // MED-56: Ensure a done chunk is emitted if the stream ended without a messageStop event
        // (e.g. premature connection close or server error).
        if (!doneEmitted)
        {
            yield return new StreamDoneChunk();
        }
    }

    // ── Request Building ────────────────────────────────────────────────────

    private static BedrockConverseRequest BuildRequest(ChatRequest request)
    {
        var result = new BedrockConverseRequest
        {
            InferenceConfig = new BedrockInferenceConfig
            {
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens
            }
        };

        // Separate system messages from conversation messages
        foreach (var msg in request.Messages)
        {
            if (msg.Role == MessageRole.System)
            {
                result.System ??= [];
                if (msg.Content is { Length: > 0 })
                {
                    result.System.Add(new BedrockSystemContent { Text = msg.Content });
                }
            }
            else if (msg.Role == MessageRole.Tool)
            {
                // Tool results go as user-role messages with toolResult content blocks
                result.Messages.Add(new BedrockMessage
                {
                    Role = "user",
                    Content =
                    [
                        new BedrockContentBlock
                        {
                            ToolResult = new BedrockToolResult
                            {
                                ToolUseId = msg.ToolCallId ?? "",
                                Content = [new BedrockToolResultContent { Text = msg.Content ?? "" }]
                            }
                        }
                    ]
                });
            }
            else
            {
                var bedrockMsg = new BedrockMessage { Role = msg.Role.Value };

                // Add image blocks before text (vision models process images before text context)
                if (msg.Images is { Count: > 0 })
                {
                    foreach (var img in msg.Images)
                    {
                        var format = MimeToBedrockFormat(img.MimeType);
                        if (format is null)
                        {
                            continue; // Skip unsupported formats silently
                        }

                        bedrockMsg.Content.Add(new BedrockContentBlock
                        {
                            Image = new BedrockImageBlock
                            {
                                Format = format,
                                Source = new BedrockImageSource { Bytes = img.Base64Data }
                            }
                        });
                    }
                }

                if (msg.Content is { Length: > 0 })
                {
                    bedrockMsg.Content.Add(new BedrockContentBlock { Text = msg.Content });
                }

                // Append tool calls as toolUse content blocks on assistant messages
                if (msg.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        using var doc = JsonDocument.Parse(tc.ArgumentsJson);
                        bedrockMsg.Content.Add(new BedrockContentBlock
                        {
                            ToolUse = new BedrockToolUse
                            {
                                ToolUseId = tc.Id,
                                Name = tc.Name,
                                Input = doc.RootElement.Clone()
                            }
                        });
                    }
                }

                result.Messages.Add(bedrockMsg);
            }
        }

        // Map tool definitions
        if (request.Tools is { Count: > 0 })
        {
            result.ToolConfig = new BedrockToolConfig();
            foreach (var tool in request.Tools)
            {
                using var schemaDoc = JsonDocument.Parse(tool.ParametersSchemaJson);
                result.ToolConfig.Tools.Add(new BedrockToolSpec
                {
                    Spec = new BedrockToolSpecInner
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        InputSchema = new BedrockInputSchema { Json = schemaDoc.RootElement.Clone() }
                    }
                });
            }
        }

        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Maps an IANA MIME type to the format string expected by Bedrock Converse.</summary>
    private static string? MimeToBedrockFormat(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => "jpeg",
        "image/png" => "png",
        "image/gif" => "gif",
        "image/webp" => "webp",
        _ => null // unsupported format — caller skips
    };

    // ── Response Mapping ────────────────────────────────────────────────────

    private static ChatResponse MapResponse(BedrockConverseResponse response)
    {
        string? textContent = null;
        List<ToolCall>? toolCalls = null;

        if (response.Output?.Message?.Content is { } blocks)
        {
            foreach (var block in blocks)
            {
                if (block.Text is { Length: > 0 })
                {
                    if (textContent is null)
                    {
                        textContent = block.Text;
                    }
                    else
                    {
                        textContent = $"{textContent}\n{block.Text}";
                    }
                }

                if (block.ToolUse is { } toolUse)
                {
                    toolCalls ??= [];
                    toolCalls.Add(new ToolCall(
                        toolUse.ToolUseId,
                        toolUse.Name,
                        toolUse.Input.GetRawText()));
                }
            }
        }

        var finishReason = response.StopReason switch
        {
            "end_turn" or "stop_sequence" => FinishReason.Stop,
            "max_tokens" => FinishReason.Length,
            "tool_use" => FinishReason.ToolCalls,
            "content_filtered" or "guardrail_intervened" => FinishReason.ContentFilter,
            _ => FinishReason.Stop
        };

        int? cacheRead = null;
        if (response.Usage?.CacheReadInputTokenCount is > 0)
        {
            cacheRead = response.Usage.CacheReadInputTokenCount;
        }

        int? cacheWrite = null;
        if (response.Usage?.CacheWriteInputTokenCount is > 0)
        {
            cacheWrite = response.Usage.CacheWriteInputTokenCount;
        }

        return new ChatResponse(
            textContent,
            toolCalls,
            finishReason,
            response.Usage?.InputTokens,
            response.Usage?.OutputTokens,
            CacheReadTokens: cacheRead,
            CacheWriteTokens: cacheWrite);
    }
}