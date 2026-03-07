using Clawsharp.Channels;
using Clawsharp.Providers;
using Clawsharp.Security;
using Clawsharp.Tools;
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
    ///     Executes tool calls returned by the LLM, applies prompt injection scanning,
    ///     and appends the tool result messages to the conversation. Shared between the
    ///     streaming and non-streaming loops. When multiple tool calls are present, they
    ///     are executed concurrently via <see cref="Task.WhenAll"/> and results are
    ///     appended in the original order for deterministic behavior.
    /// </summary>
    private async Task ExecuteToolCallsAsync(
        IReadOnlyList<ToolCall> toolCalls,
        List<ChatMessage> messages,
        CancellationToken ct)
    {
        if (toolCalls.Count == 1)
        {
            // Single tool call — no parallelism overhead needed.
            var tc = toolCalls[0];
            LogToolExecution(_logger, tc.Name, tc.ArgumentsJson[..Math.Min(100, tc.ArgumentsJson.Length)]);
            var result = await _tools.ExecuteAsync(tc.Name, tc.ArgumentsJson, ct);
            result = ApplyToolResultGuard(tc, result, ct);
            messages.Add(new ChatMessage(MessageRole.Tool, result, ToolCallId: tc.Id, Name: tc.Name));
            return;
        }

        // Multiple tool calls — execute concurrently, collect results in original order.
        var tasks = new Task<string>[toolCalls.Count];
        for (var i = 0; i < toolCalls.Count; i++)
        {
            var tc = toolCalls[i];
            LogToolExecution(_logger, tc.Name, tc.ArgumentsJson[..Math.Min(100, tc.ArgumentsJson.Length)]);
            tasks[i] = _tools.ExecuteAsync(tc.Name, tc.ArgumentsJson, ct);
        }

        var results = await Task.WhenAll(tasks);

        // Append results in original order for deterministic behavior.
        for (var i = 0; i < toolCalls.Count; i++)
        {
            var tc = toolCalls[i];
            var result = results[i];
            result = ApplyToolResultGuard(tc, result, ct);
            messages.Add(new ChatMessage(MessageRole.Tool, result, ToolCallId: tc.Id, Name: tc.Name));
        }
    }

    /// <summary>
    ///     Applies prompt injection scanning and XML wrapping to a tool result.
    ///     Returns the (possibly modified) result string.
    /// </summary>
    private string ApplyToolResultGuard(ToolCall tc, string result, CancellationToken ct)
    {
        if (!_defaults.PromptInjectionGuard) return result;

        var injAction = PromptGuard.ScanAndApply(ref result, tc.Name, "warn", _auditLogger, null, null, ct);
        if (injAction != InjectionAction.None)
        {
            LogPotentialInjection($"tool result ({tc.Name})", result[..Math.Min(50, result.Length)]);
        }

        result = PromptGuard.WrapToolResult(tc.Name, result);
        return result;
    }

    /// <summary>
    ///     Drains files queued by <see cref="PendingFileStore"/> during tool execution
    ///     and delivers them via the active channel. If the channel implements
    ///     <see cref="Channels.IFileChannel"/>, files are uploaded natively. Otherwise,
    ///     a text fallback message is sent with the filename and size.
    /// </summary>
    private async Task DeliverPendingFilesAsync(IChannel? channel, OutboundMessage outbound, CancellationToken ct)
    {
        var pending = PendingFileStore.DrainAll();
        if (pending.Count == 0 || channel is null)
        {
            return;
        }

        if (channel is Channels.IFileChannel fileChannel)
        {
            foreach (var file in pending)
            {
                try
                {
                    var ok = await fileChannel.SendFileAsync(
                        outbound.RecipientId,
                        file.Filename,
                        file.Content,
                        file.Message,
                        outbound.ThreadId,
                        ct).ConfigureAwait(false);
                    if (!ok)
                    {
                        LogPendingFileDeliveryFailed(_logger, file.Filename, "channel returned false");
                    }
                }
                catch (Exception ex)
                {
                    LogPendingFileDeliveryFailed(_logger, file.Filename, ex.Message);
                }
            }
        }
        else
        {
            // Fallback for channels without native file upload — send a summary message.
            foreach (var file in pending)
            {
                var fallbackText = $"[File: {file.Filename} ({file.Content.Length:N0} bytes)]";
                if (file.Message is not null)
                {
                    fallbackText = $"{file.Message}\n{fallbackText}";
                }

                try
                {
                    await channel.SendAsync(outbound with { Text = fallbackText }, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogPendingFileDeliveryFailed(_logger, file.Filename, ex.Message);
                }
            }
        }
    }
}