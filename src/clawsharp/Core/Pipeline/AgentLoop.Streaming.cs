using System.Text;
using System.Threading.Channels;
using Clawsharp.Channels;
using Clawsharp.Providers;
using Microsoft.Extensions.Logging;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Pipeline;

public sealed partial class AgentLoop
{
    /// <summary>
    ///     Streaming tool loop. Uses <see cref="FallbackChain.ExecuteStreamAsync" /> so that provider
    ///     fallback applies equally to streaming and non-streaming paths.
    ///     Text deltas are forwarded to the channel inline; tool calls are reconstructed from index-keyed
    ///     deltas, executed, and then the loop continues. Returns the final text reply.
    ///     When <paramref name="providerOverride"/> is non-null, it is used as the sole
    ///     streaming candidate instead of the configured fallback chain.
    /// </summary>
    private async Task<LoopResult> RunStreamingLoopAsync(
        IStreamingChannel streamingChannel,
        ChatRequest request,
        List<ChatMessage> messages,
        OutboundMessage outbound,
        Session session,
        CancellationToken ct,
        IProvider? providerOverride = null)
    {
        var streamingCandidates = providerOverride is IStreamingProvider sp
            ? [(sp.Name, sp)]
            : GetStreamingFallbackCandidates();
        long totalCacheRead = 0;
        long totalCacheWrite = 0;

        for (var iteration = 0; iteration < _defaults.MaxToolIterations; iteration++)
        {
            // Bridge the provider stream into a producer/consumer pattern so text deltas
            // flow to the channel while tool calls are accumulated in a single pass.
            var pipe = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

            // Start consuming the provider stream in the background, writing text deltas to the pipe
            // and accumulating tool calls. The pipe writer completes when the stream ends.
            var consumeTask = ConsumeProviderStreamAsync(
                streamingCandidates, request, pipe.Writer, session.ShowThinking, ct);

            // Forward text deltas to the channel while consuming.
            try
            {
                await streamingChannel.StreamAsync(outbound, pipe.Reader.ReadAllAsync(ct), ct);
            }
            catch (Exception ex)
            {
                LogStreamingChannelError(_logger, ex);
            }

            // Wait for the producer to finish accumulating tool calls.
            var result = await consumeTask;

            // Update session token counts from streaming usage data.
            session.TotalInputTokens += result.InputTokens;
            session.TotalOutputTokens += result.OutputTokens;
            totalCacheRead += result.CacheReadTokens;
            totalCacheWrite += result.CacheWriteTokens;

            // If all streaming providers were exhausted, return an error reply.
            // When text has already been streamed to the user, append the notice inline
            // so the user sees a single coherent message rather than a partial response
            // followed by a separate error.
            if (result.ExhaustedException is not null)
            {
                var errorNotice = "\n\n[Error: all configured providers became unavailable. The response above may be incomplete.]";
                if (result.Text.Length > 0)
                {
                    return new LoopResult(result.Text + errorNotice, totalCacheRead, totalCacheWrite);
                }

                return new LoopResult("Sorry, all configured providers are currently unavailable. Please try again later.", totalCacheRead,
                    totalCacheWrite);
            }

            // Reconstruct tool calls from the builders.
            var toolCalls = ReconstructToolCalls(result.ToolBuilders);
            var assistantText = result.Text.Length > 0 ? result.Text.ToString() : null;

            if (toolCalls?.Count > 0)
            {
                // Add the assistant's turn (which may include streaming text + tool calls) to history.
                messages.Add(new ChatMessage(MessageRole.Assistant, assistantText, ToolCalls: toolCalls));
                await ExecuteToolCallsAsync(toolCalls, messages, ct);

                request = request with { Messages = messages };
                continue; // next streaming iteration
            }

            // No tool calls — the streamed text IS the final reply.
            var finalReply = BuildStreamingFinalReply(assistantText, result.Thinking, session.ShowThinking);
            messages.Add(new ChatMessage(MessageRole.Assistant, finalReply));
            return new LoopResult(finalReply, totalCacheRead, totalCacheWrite);
        }

        return new LoopResult(null, totalCacheRead, totalCacheWrite); // iteration cap hit
    }

    /// <summary>Accumulated state from consuming a single streaming iteration.</summary>
    private sealed record StreamConsumeResult(
        StringBuilder Text,
        StringBuilder Thinking,
        Dictionary<int, (string Id, string Name, StringBuilder Args)> ToolBuilders,
        int InputTokens,
        int OutputTokens,
        int CacheReadTokens,
        int CacheWriteTokens,
        FallbackExhaustedException? ExhaustedException);

    /// <summary>
    ///     Consumes the provider stream, writing text deltas to <paramref name="pipeWriter"/>
    ///     for the channel consumer while accumulating tool call builders, thinking content,
    ///     and token usage. Completes the pipe writer when the stream ends (including on error).
    /// </summary>
    private async Task<StreamConsumeResult> ConsumeProviderStreamAsync(
        IReadOnlyList<(string Name, IStreamingProvider Provider)> candidates,
        ChatRequest request,
        ChannelWriter<string> pipeWriter,
        bool showThinking,
        CancellationToken ct)
    {
        var textSb = new StringBuilder();
        var thinkingSb = new StringBuilder();
        var emittedThinkingOpen = false;
        var toolBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        var inputTokens = 0;
        var outputTokens = 0;
        var cacheReadTokens = 0;
        var cacheWriteTokens = 0;
        FallbackExhaustedException? exhaustedException = null;

        try
        {
            await foreach (var chunk in _fallbackChain.ExecuteStreamAsync(candidates, request, ct, ApplyModelOverride))
            {
                switch (chunk)
                {
                    case TextDeltaChunk td:
                        // Close the thinking block if we were streaming thinking content.
                        if (emittedThinkingOpen)
                        {
                            emittedThinkingOpen = false;
                            await pipeWriter.WriteAsync("\n</thinking>\n\n", ct);
                        }

                        textSb.Append(td.Delta);
                        await pipeWriter.WriteAsync(td.Delta, ct);
                        break;

                    case ThinkingDeltaChunk tk:
                        thinkingSb.Append(tk.Delta);

                        // Stream thinking content to the channel when ShowThinking is enabled,
                        // wrapped in <thinking> tags so the frontend can parse and display them.
                        if (showThinking)
                        {
                            if (!emittedThinkingOpen)
                            {
                                emittedThinkingOpen = true;
                                await pipeWriter.WriteAsync("<thinking>\n", ct);
                            }

                            await pipeWriter.WriteAsync(tk.Delta, ct);
                        }

                        break;

                    case ToolCallChunk tc:
                        if (!toolBuilders.TryGetValue(tc.Index, out var builder))
                        {
                            builder = ("", "", new StringBuilder());
                        }

                        var id = tc.Id ?? builder.Id;
                        var name = tc.Name ?? builder.Name;
                        if (tc.ArgumentsDelta is not null)
                        {
                            builder.Args.Append(tc.ArgumentsDelta);
                        }

                        toolBuilders[tc.Index] = (id, name, builder.Args);
                        break;

                    case StreamUsageChunk usage:
                        // Anthropic emits usage in message_start (input + cache tokens)
                        // and message_delta (output tokens). Accumulate both.
                        inputTokens += usage.InputTokens;
                        outputTokens += usage.OutputTokens;
                        cacheReadTokens += usage.CacheReadTokens;
                        cacheWriteTokens += usage.CacheWriteTokens;
                        break;

                    case StreamDoneChunk:
                        // Close unclosed thinking tag before stream ends.
                        if (emittedThinkingOpen)
                        {
                            emittedThinkingOpen = false;
                            await pipeWriter.WriteAsync("\n</thinking>\n\n", ct);
                        }

                        break;
                }
            }
        }
        catch (FallbackExhaustedException ex)
        {
            exhaustedException = ex;
            LogAllProvidersExhausted(ex.Message);
            // Pipe is completed in finally — channel task will drain an empty pipe and return.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStreamingProviderError(_logger, ex);
            // If we error mid-stream still complete the pipe so the channel task unblocks.
        }
        finally
        {
            pipeWriter.Complete();
        }

        return new StreamConsumeResult(
            textSb, thinkingSb, toolBuilders,
            inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens,
            exhaustedException);
    }

    /// <summary>
    ///     Reconstructs <see cref="ToolCall"/> instances from the index-keyed builders
    ///     accumulated during streaming. Returns <c>null</c> when no tool calls were received.
    /// </summary>
    private static List<ToolCall>? ReconstructToolCalls(
        Dictionary<int, (string Id, string Name, StringBuilder Args)> toolBuilders)
    {
        if (toolBuilders.Count == 0) return null;

        return toolBuilders
               .OrderBy(kv => kv.Key)
               .Select(kv => new ToolCall(
                   kv.Value.Id,
                   kv.Value.Name,
                   kv.Value.Args.Length > 0 ? kv.Value.Args.ToString() : "{}"))
               .ToList();
    }

    /// <summary>
    ///     Builds the final reply text for a streaming iteration that produced no tool calls.
    ///     Prepends the thinking block when <paramref name="showThinking"/> is enabled and
    ///     thinking content was accumulated.
    /// </summary>
    private static string BuildStreamingFinalReply(
        string? assistantText,
        StringBuilder thinkingSb,
        bool showThinking)
    {
        var finalReply = assistantText ?? "(no response)";
        if (showThinking && thinkingSb.Length > 0)
        {
            finalReply = $"<thinking>\n{thinkingSb}\n</thinking>\n\n{finalReply}";
        }

        return finalReply;
    }
}