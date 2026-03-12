using System.Runtime.CompilerServices;
using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Providers.Anthropic;

public sealed class AnthropicProvider : IProvider, IStreamingProvider
{
    private readonly IHttpClientFactory _httpFactory;

    private readonly string _apiKey;

    public AnthropicProvider(IHttpClientFactory httpClientFactory, string apiKey, string name = "anthropic")
    {
        Name = name;
        _apiKey = apiKey;
        _httpFactory = httpClientFactory;
    }

    public string Name { get; }

    /// <inheritdoc />
    public bool SupportsVision => true;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var anthReq = BuildMessagesRequest(request, false);

        var anthResp = await ProviderHttpHelper.ExecuteAsync(
                           _httpFactory, anthReq, ConfigureHeaders, "Anthropic API", ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("Empty response from Anthropic API.");

        var textParts = new List<string>();
        var thinkingParts = new List<string>();
        var toolCalls = new List<ToolCall>();

        foreach (var block in anthResp.Content)
        {
            if (!block.TryGetProperty("type", out var typeProp))
            {
                continue;
            }

            var type = typeProp.GetString();

            if (type == "text" && block.TryGetProperty("text", out var textProp))
            {
                textParts.Add(textProp.GetString() ?? "");
            }
            else if (type == "thinking" && block.TryGetProperty("thinking", out var thinkingProp))
            {
                thinkingParts.Add(thinkingProp.GetString() ?? "");
            }
            else if (type == "tool_use")
            {
                var id = block.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
                var name = block.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                var inputJson = block.TryGetProperty("input", out var inputP)
                    ? inputP.GetRawText()
                    : "{}";
                toolCalls.Add(new ToolCall(id, name, inputJson));
            }
        }

        var finishReason = anthResp.StopReason switch
        {
            "tool_use" => FinishReason.ToolCalls,
            "end_turn" or "stop_sequence" => FinishReason.Stop,
            "max_tokens" => FinishReason.Length,
            _ => FinishReason.Stop
        };

        return new ChatResponse(
            textParts.Count > 0 ? string.Join("\n", textParts) : null,
            toolCalls.Count > 0 ? toolCalls : null,
            finishReason,
            anthResp.Usage?.InputTokens,
            anthResp.Usage?.OutputTokens,
            thinkingParts.Count > 0 ? string.Join("\n", thinkingParts) : null,
            CacheReadTokens: anthResp.Usage?.CacheReadInputTokens,
            CacheWriteTokens: anthResp.Usage?.CacheCreationInputTokens
        );
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var anthReq = BuildMessagesRequest(request, true);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(anthReq, AnthropicJsonContext.Default.MessagesRequest);

        var (http, resp, body) = await ProviderHttpHelper.SendStreamingAsync(
            _httpFactory, anthReq.Url, jsonBytes, ConfigureHeaders, "Anthropic API stream", ct).ConfigureAwait(false);

        using var _ = http;
        using var __ = resp;
        await using var stream = body;

        var doneEmitted = false;

        await foreach (var (eventName, data) in SseLineReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            StreamEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize(data, AnthropicJsonContext.Default.StreamEvent);
            }
            catch (JsonException)
            {
                continue;
            }

            if (evt is null)
            {
                continue;
            }

            // Use the type from within the data payload (more reliable than the SSE event: line).
            var evtType = evt.Type ?? eventName;

            switch (evtType)
            {
                case "message_start":
                {
                    // message_start carries usage with input_tokens, cache_read_input_tokens,
                    // cache_creation_input_tokens (output_tokens is 0 at this point).
                    var u = evt.Message?.Usage;
                    if (u is not null)
                    {
                        yield return new StreamUsageChunk(
                            u.InputTokens,
                            u.OutputTokens,
                            u.CacheReadInputTokens,
                            u.CacheCreationInputTokens);
                    }

                    break;
                }

                case "content_block_start":
                    // Emits tool-use block header (id + name) — yields ToolCallChunk with id/name, no args delta.
                    if (evt.ContentBlock?.Type == "tool_use" && evt.Index.HasValue)
                    {
                        yield return new ToolCallChunk(
                            evt.Index.Value,
                            evt.ContentBlock.Id,
                            evt.ContentBlock.Name,
                            null
                        );
                    }

                    break;

                case "content_block_delta":
                    if (evt.Delta is null || !evt.Index.HasValue)
                    {
                        break;
                    }

                    if (evt.Delta.Type == "text_delta" && evt.Delta.Text is { Length: > 0 } text)
                    {
                        yield return new TextDeltaChunk(text);
                    }
                    else if (evt.Delta.Type == "input_json_delta" && evt.Delta.PartialJson is not null)
                    {
                        yield return new ToolCallChunk(
                            evt.Index.Value,
                            null,
                            null,
                            evt.Delta.PartialJson
                        );
                    }
                    else if (evt.Delta.Type == "thinking_delta" && evt.Delta.ThinkingDelta is { Length: > 0 } thought)
                    {
                        yield return new ThinkingDeltaChunk(thought);
                    }

                    break;

                case "message_delta":
                {
                    // message_delta carries usage with output_tokens (the final count).
                    var u = evt.Usage;
                    if (u is not null)
                    {
                        yield return new StreamUsageChunk(
                            u.InputTokens,
                            u.OutputTokens,
                            u.CacheReadInputTokens,
                            u.CacheCreationInputTokens);
                    }

                    break;
                }

                case "message_stop":
                    doneEmitted = true;
                    yield return new StreamDoneChunk();
                    yield break;
            }
        }

        if (!doneEmitted)
        {
            yield return new StreamDoneChunk();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds Anthropic-specific auth headers to an outgoing HTTP request.
    /// OAuth setup tokens (<c>sk-ant-oat01-</c> prefix) are sent as <c>Authorization: Bearer</c>;
    /// standard API keys use the <c>x-api-key</c> header.
    /// </summary>
    private void ConfigureHeaders(HttpRequestMessage req)
    {
        if (_apiKey.StartsWith("sk-ant-oat01-", StringComparison.Ordinal))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }
        else
        {
            req.Headers.Add("x-api-key", _apiKey);
        }

        req.Headers.Add(ClawsharpConstants.HttpHeaders.AnthropicApiVersion, ClawsharpConstants.AnthropicVersion);
    }

    /// <summary>
    /// Normalizes model IDs for the Anthropic API.
    /// Dots are replaced with hyphens: "claude-sonnet-4.6" becomes "claude-sonnet-4-6".
    /// </summary>
    private static string NormalizeModelId(string model)
    {
        if (model.StartsWith("claude", StringComparison.OrdinalIgnoreCase) && model.Contains('.'))
        {
            return model.Replace('.', '-');
        }

        return model;
    }

    // TODO: optimize BuildMessagesRequest to avoid intermediate JSON string allocations.
    // Currently we serialize individual blocks to JSON strings, concatenate them into a JSON
    // array string, then re-parse with JsonDocument.Parse. This could be replaced by writing
    // directly to a Utf8JsonWriter or building JsonElement arrays via JsonArray.
    private static MessagesRequest BuildMessagesRequest(ChatRequest request, bool stream)
    {
        string? systemText = null;
        var userMessages = new List<ConversationMessage>();

        foreach (var msg in request.Messages)
        {
            if (msg.Role == MessageRole.System)
            {
                systemText = msg.Content;
                continue;
            }

            if (msg.Role == MessageRole.Tool)
            {
                var toolResultBlock = new ToolResultBlock
                {
                    ToolUseId = msg.ToolCallId ?? "",
                    Content = msg.Content ?? "",
                    IsError = false
                };
                var toolResultJson = JsonSerializer.Serialize(toolResultBlock, AnthropicJsonContext.Default.ToolResultBlock);
                var arr = $"[{toolResultJson}]";
                using var arrDoc = JsonDocument.Parse(arr);
                userMessages.Add(new ConversationMessage { Role = "user", Content = arrDoc.RootElement.Clone() });
                continue;
            }

            if (msg.ToolCalls?.Count > 0)
            {
                var blocks = new List<string>();
                if (msg.Content is not null)
                {
                    var textBlock = new TextBlock { Text = msg.Content };
                    blocks.Add(JsonSerializer.Serialize(textBlock, AnthropicJsonContext.Default.TextBlock));
                }

                foreach (var tc in msg.ToolCalls)
                {
                    using var inputDoc = JsonDocument.Parse(tc.ArgumentsJson);
                    var toolUse = new ToolUseBlock { Id = tc.Id, Name = tc.Name, Input = inputDoc.RootElement.Clone() };
                    blocks.Add(JsonSerializer.Serialize(toolUse, AnthropicJsonContext.Default.ToolUseBlock));
                }

                var blocksJson = $"[{string.Join(",", blocks)}]";
                using var blocksDoc = JsonDocument.Parse(blocksJson);
                userMessages.Add(new ConversationMessage { Role = "assistant", Content = blocksDoc.RootElement.Clone() });
                continue;
            }

            // For user messages with images, build a multi-block content array.
            if (msg.Role == MessageRole.User && msg.Images is { Count: > 0 } images)
            {
                var blocks = new List<string>(images.Count + 1);
                foreach (var img in images)
                {
                    var imageBlock = new ImageBlock
                    {
                        Source = new ImageSource { MediaType = img.MimeType, Data = img.Base64Data }
                    };
                    blocks.Add(JsonSerializer.Serialize(imageBlock, AnthropicJsonContext.Default.ImageBlock));
                }

                if (msg.Content is { Length: > 0 } imgText)
                {
                    var textBlock = new TextBlock { Text = imgText };
                    blocks.Add(JsonSerializer.Serialize(textBlock, AnthropicJsonContext.Default.TextBlock));
                }

                var blocksJson = $"[{string.Join(",", blocks)}]";
                using var blocksDoc = JsonDocument.Parse(blocksJson);
                userMessages.Add(new ConversationMessage { Role = msg.Role.Value, Content = blocksDoc.RootElement.Clone() });
                continue;
            }

            var textJson = JsonSerializer.Serialize(msg.Content ?? "", AnthropicJsonContext.Default.String);
            using var textDoc = JsonDocument.Parse(textJson);
            userMessages.Add(new ConversationMessage { Role = msg.Role.Value, Content = textDoc.RootElement.Clone() });
        }

        List<ToolDefinition>? anthTools = null;
        if (request.Tools?.Count > 0)
        {
            anthTools = request.Tools.Select(t =>
            {
                using var schemaDoc = JsonDocument.Parse(t.ParametersSchemaJson);
                return new ToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = schemaDoc.RootElement.Clone()
                };
            }).ToList();

            // Cache the entire tools array by marking the last entry.
            // Anthropic caches everything up to and including the marked tool.
            // Only annotate when the caller opted in (CachingConfig.CacheToolDefinitions = true).
            if (request.CacheToolDefinitions)
            {
                anthTools[^1].CacheControl = new CacheControl();
            }
        }

        // When extended thinking is enabled, Anthropic requires temperature to be absent
        // and max_tokens must accommodate the thinking budget.
        AnthropicThinking? thinking = null;
        float? temperature = request.Temperature;
        var maxTokens = request.MaxTokens ?? 4096;

        if (request.ThinkingBudgetTokens > 0)
        {
            thinking = new AnthropicThinking { BudgetTokens = request.ThinkingBudgetTokens };
            temperature = null; // Anthropic rejects requests with both thinking and temperature
            // Ensure max_tokens is at least budget_tokens + a reasonable output buffer.
            if (maxTokens < request.ThinkingBudgetTokens + 1024)
            {
                maxTokens = request.ThinkingBudgetTokens + 4096;
            }
        }

        return new MessagesRequest
        {
            Model = NormalizeModelId(request.Model),
            MaxTokens = maxTokens,
            Temperature = temperature,
            System = BuildSystemBlocks(request.SystemStaticPart, request.SystemDynamicPart, systemText),
            Messages = userMessages,
            Tools = anthTools,
            Thinking = thinking,
            Stream = stream
        };
    }

    /// <summary>
    /// Builds the <c>system</c> block array for the Anthropic API.
    /// <para>
    /// When <paramref name="staticPart"/> is provided (via <see cref="ChatRequest.SystemStaticPart"/>),
    /// it is marked with <c>cache_control: {type: "ephemeral"}</c> so Anthropic caches the stable
    /// prefix (identity, instructions, tools, memory). The <paramref name="dynamicPart"/> (datetime,
    /// channel) is appended as a plain uncached block.
    /// </para>
    /// <para>
    /// Falls back to a single uncached block containing <paramref name="fallbackText"/> when
    /// the split parts are not available (e.g. sub-agents or test paths that use the old API).
    /// </para>
    /// </summary>
    private static List<AnthropicSystemBlock>? BuildSystemBlocks(
        string? staticPart,
        string? dynamicPart,
        string? fallbackText)
    {
        // Prefer the explicit split parts set by SystemPromptBuilder.BuildSplit().
        if (!string.IsNullOrEmpty(staticPart) || !string.IsNullOrEmpty(dynamicPart))
        {
            var blocks = new List<AnthropicSystemBlock>(2);

            if (!string.IsNullOrEmpty(staticPart))
            {
                blocks.Add(new AnthropicSystemBlock
                {
                    Text = staticPart,
                    CacheControl = new CacheControl()
                });
            }

            if (!string.IsNullOrEmpty(dynamicPart))
            {
                blocks.Add(new AnthropicSystemBlock { Text = dynamicPart });
            }

            return blocks.Count > 0 ? blocks : null;
        }

        // Fallback: single uncached block (no caching benefit, but keeps existing behaviour).
        if (!string.IsNullOrEmpty(fallbackText))
        {
            return [new AnthropicSystemBlock { Text = fallbackText }];
        }

        return null;
    }
}