using System.Diagnostics;
using System.Text;
using Clawsharp.Channels;
using Clawsharp.Cost;
using Clawsharp.Features.Chat.Commands;
using Clawsharp.Features.Chat.Queries;
using Clawsharp.Features.Cost.Commands;
using Clawsharp.Features.Cost.Queries;
using Clawsharp.Features.Memory.Commands;
using Clawsharp.Features.Session.Commands;
using Clawsharp.Providers;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;

namespace Clawsharp.Core.Pipeline;

public sealed partial class AgentLoop
{
    private const int FlushSnippetMaxLength = 300;

    private const int ConsolidateSnippetMaxLength = 200;

    private const int FlushMaxMessages = 30;

    // ──────────────────────────────────────────────────────────────────────
    //  Pipeline stage 2: Context window guard
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Applies context window enforcement: emergency force-compress or normal compaction
    ///     when token usage approaches the model's context limit.
    /// </summary>
    private async Task<List<ChatMessage>> ApplyContextWindowGuardAsync(
        Session session,
        BuildChatRequest.Result ctx,
        List<ChatMessage> messages,
        CancellationToken ct)
    {
        if (!ctx.ContextWindowEnabled)
        {
            return messages;
        }

        var cwConfig = _defaults.ContextWindow!;
        var contextWindow = ContextWindowGuard.ResolveContextWindow(_defaults.Model, cwConfig.ContextWindowTokens);
        var estimated = ContextWindowGuard.EstimateTokens(messages);

        if (ContextWindowGuard.IsEmergency(estimated, contextWindow, cwConfig.EmergencyTrimPercent))
        {
            LogContextWindowEmergency(estimated, contextWindow);
            messages = CompactionService.ForceCompress(messages);
            session.Messages.Clear();
            session.Messages.AddRange(messages.Where(m => m.Role != MessageRole.System));
            await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct).ConfigureAwait(false);
        }
        else if (ContextWindowGuard.ShouldCompact(estimated, contextWindow, cwConfig.CompactionTriggerPercent))
        {
            var compConfig = _defaults.Compaction ?? new CompactionConfig();
            if (compConfig.Enabled)
            {
                LogContextWindowCompacting(estimated, contextWindow);

                if (compConfig.PreCompactionMemoryFlush)
                {
                    var recentStart = Math.Max(1, messages.Count - compConfig.KeepRecent);
                    List<ChatMessage> aboutToDiscard;
                    if (recentStart > 1)
                    {
                        aboutToDiscard = messages.GetRange(1, recentStart - 1);
                    }
                    else
                    {
                        aboutToDiscard = [];
                    }
                    await FlushMemoryBeforeCompactionAsync(aboutToDiscard, ct).ConfigureAwait(false);
                }

                messages = await _compactionService.CompactAsync(
                    messages, _provider, _defaults.Model,
                    compConfig.KeepRecent, compConfig.MaxSummaryChars, compConfig.MaxSourceChars, ct).ConfigureAwait(false);
                session.Messages.Clear();
                session.Messages.AddRange(messages.Where(m => m.Role != MessageRole.System));
                await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct).ConfigureAwait(false);
            }
        }

        return messages;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Pipeline stage 4: Dispatch to provider
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Checks budget, builds the <see cref="ChatRequest"/>, dispatches to streaming or
    ///     non-streaming provider loop, and records cost. Returns null when the budget is exceeded
    ///     (rejection is sent to the channel inline).
    /// </summary>
    private async Task<LoopResult?> DispatchToProviderAsync(
        string sessionId,
        Session session,
        BuildChatRequest.Result ctx,
        List<ChatMessage> messages,
        OutboundMessage outbound,
        IChannel? channel,
        InboundMessage inbound,
        CancellationToken ct)
    {
        // Model routing — delegates to the RouteModel handler which handles overrides,
        // intelligent complexity scoring, and simple/primary model selection.
        var routeResult = await _handlers.RouteModel.HandleAsync(
            new RouteModel.Query(
                UserText: messages.LastOrDefault(m => m.Role == MessageRole.User)?.Content ?? "",
                RecentToolCalls: session.Messages.TakeLast(5).Count(m => m.Role == MessageRole.Tool),
                ConversationDepth: session.Messages.Count,
                ModelOverride: inbound.ModelOverride), ct).ConfigureAwait(false);
        var actualModel = routeResult.Model;

        // Budget check — reject before calling the provider if limits are exceeded.
        // Estimate input cost from the current message list (output tokens unknown pre-call).
        // Unknown models return price (0, 0) from DefaultPricing, making the check a soft pass-through.
        var estimatedInputTokens = ContextWindowGuard.EstimateTokens(messages);
        var (inputPer1M, _) = DefaultPricing.GetPrice(actualModel);
        var estimatedCost = estimatedInputTokens * inputPer1M / 1_000_000m;
        var budgetCheck = await _handlers.CheckBudget.HandleAsync(
            new CheckBudget.Query(estimatedCost), ct).ConfigureAwait(false);
        if (budgetCheck.Status == BudgetStatus.Exceeded)
        {
            LogBudgetExceededReject(sessionId, budgetCheck.Message ?? "Budget exceeded");
            if (channel is not null)
            {
                await channel.SendAsync(
                    outbound with { Text = $"Request blocked: {budgetCheck.Message}" }, ct).ConfigureAwait(false);
            }

            return null;
        }

        // Merge consecutive same-role messages to prevent Anthropic/OpenAI-compat rejections.
        messages = MergeConsecutiveRoles(messages);

        var request = BuildChatRequestFromContext(actualModel, messages, ctx, _defaults);

        // Snapshot token counts so we can compute per-request delta for cost tracking.
        var inputTokensBefore = session.TotalInputTokens;
        var outputTokensBefore = session.TotalOutputTokens;

        // Provider override (e.g. from cron job config): create a one-off provider
        // and use it as the sole candidate, bypassing the default fallback chain.
        var overrideProvider = CreateOverrideProvider(inbound.ProviderOverride);

        // Use streaming when both the active provider and the channel support it.
        var activeProvider = overrideProvider ?? _provider;
        var sw = Stopwatch.StartNew();
        LoopResult loopResult;
        if (activeProvider is IStreamingProvider && channel is IStreamingChannel streamingChannel)
        {
            loopResult = await RunStreamingLoopAsync(
                streamingChannel, request, messages, outbound, session, ct,
                overrideProvider).ConfigureAwait(false);
        }
        else
        {
            loopResult = await RunNonStreamingLoopAsync(
                request, messages, session, ct, overrideProvider).ConfigureAwait(false);
        }

        sw.Stop();

        // Record cost usage — use the actual model (may differ from _defaults.Model due to routing).
        // For streaming responses the delta may be 0 (providers don't always report counts).
        var inputDelta = session.TotalInputTokens - inputTokensBefore;
        var outputDelta = session.TotalOutputTokens - outputTokensBefore;
        await _handlers.RecordUsage.HandleAsync(new RecordUsage.Command(
            sessionId, actualModel, inputDelta, outputDelta,
            loopResult.CacheRead, loopResult.CacheWrite), ct).ConfigureAwait(false);

        // Record interaction analytics (fire-and-forget — must not block the response pipeline).
        if (_analyticsEnabled && loopResult.Reply is not null)
        {
            var userPrompt = messages.LastOrDefault(m => m.Role == MessageRole.User)?.Content ?? "";
            _ = Task.Run(async () =>
            {
                try
                {
                    await _interactionTracker.RecordAsync(
                        sessionId,
                        inbound.Channel.Value,
                        actualModel,
                        userPrompt,
                        loopResult.Thinking,
                        loopResult.Reply,
                        loopResult.ToolCallSummaries,
                        loopResult.ToolIterations,
                        inputDelta,
                        outputDelta,
                        loopResult.CacheRead,
                        loopResult.CacheWrite,
                        sw.ElapsedMilliseconds,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record interaction analytics");
                }
            });
        }

        return loopResult;
    }

    /// <summary>
    ///     Constructs a <see cref="ChatRequest"/> from the routed model, current messages,
    ///     and the pre-built request context. Pure function — no side effects.
    /// </summary>
    private static ChatRequest BuildChatRequestFromContext(
        string model,
        List<ChatMessage> messages,
        BuildChatRequest.Result ctx,
        AgentDefaults defaults)
    {
        var thinking = defaults.Thinking;
        IReadOnlyList<ToolDefinition>? tools = null;
        if (ctx.ToolDefinitions.Count > 0)
        {
            tools = ctx.ToolDefinitions;
        }

        string? staticPart = null;
        string? dynamicPart = null;
        if (ctx.CachingEnabled)
        {
            staticPart = ctx.StaticPrompt;
            dynamicPart = ctx.DynamicPrompt;
        }

        return new ChatRequest(
            Model: model,
            Messages: messages,
            Tools: tools,
            Temperature: defaults.Temperature,
            SystemStaticPart: staticPart,
            SystemDynamicPart: dynamicPart,
            CacheToolDefinitions: ctx.CacheToolDefs,
            ThinkingBudgetTokens: thinking?.BudgetTokens ?? 0,
            ReasoningEffort: thinking?.ReasoningEffort?.Value,
            GeminiThinkingBudget: thinking?.GeminiBudgetTokens ?? 0
        );
    }

    /// <summary>
    ///     Creates a one-off provider from a provider name override (e.g. from cron job config).
    ///     Returns <c>null</c> if <paramref name="providerName"/> is null or creation fails.
    /// </summary>
    private IProvider? CreateOverrideProvider(string? providerName)
    {
        if (providerName is null)
        {
            return null;
        }

        try
        {
            return ProviderFactory.Create(
                providerName, _appConfig.Providers, _httpClientFactory, deviceFlow: null);
        }
        catch (Exception ex)
        {
            LogProviderError(_logger, ex);
            // Fall through to the default provider if the override fails to create.
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Pipeline stage 5: Post-process reply
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Applies leak detection, updates and saves the session, sends the reply to non-streaming
    ///     channels, and triggers memory consolidation.
    /// </summary>
    private async Task PostProcessReplyAsync(
        InboundMessage inbound,
        Session session,
        LoopResult loopResult,
        bool contextWindowEnabled,
        OutboundMessage outbound,
        IChannel? channel,
        CanaryGuard? canaryGuard,
        CancellationToken ct)
    {
        var finalReply = loopResult.Reply ?? "(max tool iterations reached)";

        // Canary exfiltration check + leak detection via SanitizeReply handler.
        var sanitizeResult = await _handlers.SanitizeReply.HandleAsync(
            new SanitizeReply.Command(finalReply, canaryGuard, inbound.Channel.Value, inbound.SenderId), ct).ConfigureAwait(false);
        finalReply = sanitizeResult.SanitizedReply;

        // Update session — images are not persisted to keep session files small.
        string userTextForHistory;
        if (inbound.Images is { Count: > 0 })
        {
            userTextForHistory = $"{inbound.Text} [+{inbound.Images.Count} image(s)]".TrimStart();
        }
        else
        {
            userTextForHistory = inbound.Text;
        }
        var now = DateTimeOffset.UtcNow;
        session.Messages.Add(new ChatMessage(MessageRole.User, userTextForHistory, Timestamp: now));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, finalReply, Timestamp: now));
        session.TotalMessageCount += 2;

        // Trim session messages — skip count-based trim when context window guard handles it.
        if (!contextWindowEnabled && session.Messages.Count > _defaults.MaxContextMessages)
        {
            session.Messages.RemoveRange(0, session.Messages.Count - _defaults.MaxContextMessages);
        }

        try
        {
            await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Session loss is recoverable on next load; reply loss is not.
            // Log and continue so the user still receives their reply.
            LogSessionSaveFailed(ex, session.Id);
        }

        // Send reply first — user gets response immediately.
        // Only needed for the non-streaming path or if streaming was bypassed.
        // The streaming path delivers text inline via StreamAsync; we still call SendAsync
        // here only when channel is NOT a streaming channel (ordinary send path).
        if (channel is not null && channel is not IStreamingChannel)
        {
            await channel.SendAsync(outbound with { Text = finalReply }, ct).ConfigureAwait(false);
        }

        // Deliver any files queued by SendFileTool during the tool-call loop.
        await DeliverPendingFilesAsync(channel, outbound, ct).ConfigureAwait(false);

        // Memory consolidation — awaited after send to prevent concurrent session access.
        if (_defaults.ConsolidateEvery > 0 && session.TotalMessageCount % _defaults.ConsolidateEvery == 0)
        {
            await ConsolidateMemoryAsync(session, ct).ConfigureAwait(false);
        }

        // Post-turn durable fact extraction — accumulate turns and periodically extract facts.
        TriggerFactExtraction(session.Id, inbound.Text, finalReply);
    }

    /// <summary>
    ///     Accumulates the current turn into the fact extractor buffer and, when the
    ///     threshold is met, fires off a background fact extraction task. Fire-and-forget
    ///     by design — fact extraction must not block the response pipeline.
    /// </summary>
    private void TriggerFactExtraction(string sessionId, string userText, string reply)
    {
        var factExConfig = _memoryConfig.FactExtraction;
        if (factExConfig is not { Enabled: true })
        {
            return;
        }

        if (!_factExtractor.AccumulateTurn(sessionId, userText, reply))
        {
            return;
        }

        var conversationText = _factExtractor.DrainBuffer(sessionId);
        if (conversationText is null)
        {
            return;
        }

        // Fire-and-forget with observed exceptions: fact extraction should not block the response pipeline.
        _ = Task.Run(async () =>
        {
            try
            {
                await _handlers.ExtractFacts.HandleAsync(
                    new ExtractFacts.Command(conversationText), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-turn fact extraction failed");
            }
        });
    }

    private async Task FlushMemoryBeforeCompactionAsync(
        List<ChatMessage> messagesToDiscard,
        CancellationToken ct)
    {
        if (messagesToDiscard.Count == 0)
        {
            return;
        }

        try
        {
            const string flushPrompt =
                "Before this conversation is summarized, extract any important facts, decisions, " +
                "preferences, or context that should be remembered long-term. Output only the " +
                "facts as a bullet-point list. If nothing important, output only: (nothing to save)";

            var sb = new StringBuilder();
            foreach (var m in messagesToDiscard.TakeLast(FlushMaxMessages))
            {
                var snippet = "";
                if (m.Content is { Length: > 0 } c)
                {
                    snippet = c[..Math.Min(FlushSnippetMaxLength, c.Length)];
                }

                sb.AppendLine($"{m.Role.Value}: {snippet}");
            }

            var flushReq = new ChatRequest(
                Model: _defaults.Model,
                Messages:
                [
                    new ChatMessage(MessageRole.System, flushPrompt),
                    new ChatMessage(MessageRole.User, sb.ToString())
                ],
                Temperature: 0.2f,
                MaxTokens: 800
            );

            var resp = await _provider.ChatAsync(flushReq, ct);
            if (resp.Content is { Length: > 0 } facts &&
                !facts.Contains("(nothing to save)", StringComparison.OrdinalIgnoreCase))
            {
                // Scrub secrets from extracted facts before persisting to memory
                var scrubResult = LeakDetector.Scan(facts, 0.5);
                if (!scrubResult.IsClean)
                {
                    LogFactSecretsScrubbed(scrubResult.Patterns.Count);
                    facts = scrubResult.Redacted;
                }

                await _memory.AppendHistoryAsync(facts, ct);
                LogPreCompactionFlushComplete(messagesToDiscard.Count, facts.Length);
            }
        }
        catch (Exception ex)
        {
            LogPreCompactionFlushFailed(ex);
        }
    }

    private async Task ConsolidateMemoryAsync(Session session, CancellationToken ct)
    {
        try
        {
            // Build a mini-summary from the last N messages
            var recentMessages = session.Messages.TakeLast(20).ToList();
            var sb = new StringBuilder("Summarize the key facts and decisions from this conversation in 3-5 bullet points:\n\n");
            foreach (var m in recentMessages)
            {
                var snippet = "";
                if (m.Content is { Length: > 0 } c)
                {
                    snippet = c[..Math.Min(ConsolidateSnippetMaxLength, c.Length)];
                }

                sb.AppendLine($"{m.Role.Value}: {snippet}");
            }

            var summaryRequest = new ChatRequest(
                Model: _defaults.Model,
                Messages: [new ChatMessage(MessageRole.User, sb.ToString())],
                Temperature: 0.3f,
                MaxTokens: 500
            );

            var summaryResp = await _provider.ChatAsync(summaryRequest, ct);
            if (summaryResp.Content is { Length: > 0 } summary)
            {
                // Scrub secrets from LLM summary before persisting to memory
                var scrubResult = LeakDetector.Scan(summary, 0.5);
                if (!scrubResult.IsClean)
                {
                    LogConsolidationSecretsScrubbed(scrubResult.Patterns.Count);
                    summary = scrubResult.Redacted;
                }

                await _memory.AppendHistoryAsync(summary, ct);
            }
        }
        catch (Exception ex)
        {
            LogMemoryConsolidationError(_logger, ex);
        }
    }
}