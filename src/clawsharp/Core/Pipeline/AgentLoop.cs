using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Threading.Channels;
using Clawsharp.Analytics;
using Clawsharp.Channels;
using Clawsharp.Config;
using Clawsharp.Cost;
using Clawsharp.Features.Chat.Commands;
using Clawsharp.Features.Chat.Queries;
using Clawsharp.Features.Memory.Queries;
using Clawsharp.Features.Session.Commands;
using Clawsharp.Features.Session.Queries;
using Clawsharp.Goals;
using Clawsharp.Memory;
using Clawsharp.Providers;
using Clawsharp.Security;
using Clawsharp.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Memory;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Pipeline;

public sealed partial class AgentLoop
{
    private readonly IReadOnlyList<IChannel> _channels;

    private readonly FrozenDictionary<ChannelName, IChannel> _channelMap;

    private readonly AppConfig _appConfig;

    private readonly CompactionService _compactionService;

    private readonly CostTracker _costTracker;

    private readonly AgentDefaults _defaults;

    private readonly AuditLogger _auditLogger;

    private readonly FallbackChain _fallbackChain;

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ILogger<AgentLoop> _logger;

    private readonly IMemory _memory;

    private readonly IProvider _provider;

    private readonly RateLimiter _rateLimiter;

    // One channel per session — serializes messages from the same conversation
    // while letting different sessions run concurrently.
    // Lazy<T> ensures the factory runs exactly once even if two threads race on the same key.
    private readonly ConcurrentDictionary<string, Lazy<(Channel<InboundMessage> Ch, Task DrainTask)>> _sessionPipelines = new();

    private readonly SessionStore _sessions;

    private readonly IToolRegistry _tools;

    private readonly GoalStorage _goalStorage;

    private readonly FactExtractor _factExtractor;

    // ── Immediate.Handlers (VSA/CQRS) — grouped behind AgentHandlers ──
    private readonly AgentHandlers _handlers;

    private readonly MemoryConfig _memoryConfig;

    private readonly string _workspacePath;

    private readonly SuspicionTracker _suspicionTracker = new();

    private readonly InteractionTracker _interactionTracker;

    private readonly bool _analyticsEnabled;

    /// <summary>Lazily-built candidate list for provider fallback. Built once on first use.</summary>
    private IReadOnlyList<(string Name, IProvider Provider)>? _fallbackCandidates;

    /// <summary>Lazily-built candidate list filtered to streaming providers only. Built alongside <see cref="_fallbackCandidates"/>.</summary>
    private IReadOnlyList<(string Name, IStreamingProvider Provider)>? _streamingFallbackCandidates;

    /// <summary>Per-fallback model overrides keyed by provider name. Built alongside <see cref="_fallbackCandidates"/>.</summary>
    private Dictionary<string, string>? _fallbackModelOverrides;

    /// <summary>Result of a single tool-loop execution (streaming or non-streaming).</summary>
    private sealed record LoopResult(
        string? Reply,
        long CacheRead,
        long CacheWrite,
        string? Thinking = null,
        IReadOnlyList<ToolCallSummary>? ToolCallSummaries = null,
        int ToolIterations = 0);


    public AgentLoop(
        IProvider provider,
        IReadOnlyList<IChannel> channels,
        IToolRegistry tools,
        IMemory memory,
        SessionStore sessions,
        CompactionService compactionService,
        CostTracker costTracker,
        IOptions<AgentDefaults> defaultsOptions,
        IOptions<AppConfig> configOptions,
        RateLimiter rateLimiter,
        FallbackChain fallbackChain,
        IHttpClientFactory httpClientFactory,
        ILogger<AgentLoop> logger,
        AuditLogger auditLogger,
        GoalStorage goalStorage,
        FactExtractor factExtractor,
        InteractionTracker interactionTracker,
        AgentHandlers handlers)
    {
        _provider = provider;
        _channels = channels;
        _channelMap = channels.ToFrozenDictionary(c => c.Name);
        _tools = tools;
        _memory = memory;
        _sessions = sessions;
        _compactionService = compactionService;
        _costTracker = costTracker;
        _defaults = defaultsOptions.Value;
        _appConfig = configOptions.Value;
        _memoryConfig = _appConfig.Memory;
        _workspacePath = ConfigLoader.ExpandHome(_appConfig.Tools.Workspace);
        _rateLimiter = rateLimiter;
        _fallbackChain = fallbackChain;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _auditLogger = auditLogger;
        _goalStorage = goalStorage;
        _factExtractor = factExtractor;
        _interactionTracker = interactionTracker;
        _analyticsEnabled = configOptions.Value.Analytics?.Enabled ?? false;
        _handlers = handlers;
    }

    public async Task RunAsync(IMessageBus bus, CancellationToken ct = default)
    {
        // Dispatch each inbound message to the owning session's pipeline.
        // Lazy<T> guarantees StartSessionPipeline runs exactly once per key,
        // even if multiple threads race on GetOrAdd for the same session.
        await foreach (var inbound in bus.ReadAllAsync(ct))
        {
            var sessionId = $"{inbound.Channel.Value}:{inbound.SenderId}";
            var lazy = _sessionPipelines.GetOrAdd(sessionId,
                k => new Lazy<(Channel<InboundMessage>, Task)>(() => StartSessionPipeline(k, ct)));
            await lazy.Value.Ch.Writer.WriteAsync(inbound, ct);
        }

        // Await all drain tasks so exceptions are observed on shutdown.
        // Use a 5-second timeout so in-flight LLM calls don't block exit.
        foreach (var kvp in _sessionPipelines)
        {
            if (kvp.Value.IsValueCreated)
            {
                try
                {
                    await kvp.Value.Value.DrainTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
                }
                catch (TimeoutException)
                {
                    // In-flight work didn't finish in time — abandon it.
                }
            }
        }
    }

    /// <summary>Creates a per-session unbounded channel and launches its drain loop.</summary>
    private (Channel<InboundMessage>, Task) StartSessionPipeline(string sessionId, CancellationToken ct)
    {
        // SingleWriter = true: only the RunAsync dispatch loop writes.
        // SingleReader = true: only DrainSessionAsync reads.
        var ch = Channel.CreateUnbounded<InboundMessage>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        var drainTask = DrainSessionAsync(sessionId, ch.Reader, ct);
        return (ch, drainTask);
    }

    /// <summary>
    ///     Processes all messages for one session in arrival order.
    ///     Runs until the channel is completed or <paramref name="ct" /> is cancelled.
    /// </summary>
    private async Task DrainSessionAsync(string sessionId, ChannelReader<InboundMessage> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var inbound in reader.ReadAllAsync(ct))
            {
                await ProcessMessageAsync(inbound, ct);
            }
        }
        finally
        {
            _sessionPipelines.TryRemove(sessionId, out _);
        }
    }

    internal async Task ProcessMessageAsync(InboundMessage inbound, CancellationToken ct)
    {
        var sessionId = $"{inbound.Channel.Value}:{inbound.SenderId}";
        var lag = DateTimeOffset.UtcNow - (inbound.ArrivedAt ?? DateTimeOffset.UtcNow);
        LogProcessingMessage(_logger, sessionId, lag.TotalMilliseconds);

        // Reset per-request suspicion tracker for cumulative tool result analysis.
        _suspicionTracker.Reset();

        // Track cron execution context so tools (e.g. CronTool) can detect self-scheduling loops.
        var isCron = string.Equals(inbound.SenderName, CronSenderName.Cron, StringComparison.Ordinal);
        CronContext.IsInCronExecution = isCron;

        _channelMap.TryGetValue(inbound.Channel, out var channel);
        var outbound = new OutboundMessage(
            Channel: inbound.Channel,
            RecipientId: inbound.SenderId,
            Text: "", // placeholder — overwritten before use
            ThreadId: inbound.ThreadId
        );

        // M-06: Per-IP rate limit checked first — avoids consuming a session slot
        // when the IP is already blocked. Skipped when SenderIp is null (CLI, IRC, etc.).
        if (!inbound.IsHeartbeat && !_rateLimiter.TryAcquireByIp(inbound.SenderIp))
        {
            LogIpRateLimited(_logger, inbound.SenderIp?.ToString() ?? "unknown");
            if (channel is not null)
            {
                var rateLimitMessage = outbound with { Text = "Too many requests from your network. Please wait a moment." };
                await channel.SendAsync(rateLimitMessage, ct).ConfigureAwait(false);
            }

            return;
        }

        // Per-session rate-limit check (heartbeat messages bypass rate limiting)
        if (!inbound.IsHeartbeat && !_rateLimiter.TryAcquire(sessionId))
        {
            LogRateLimited(_logger, sessionId);
            if (channel is not null)
            {
                var rateLimitMessage = outbound with { Text = "You're sending messages too fast. Please wait a moment." };
                await channel.SendAsync(rateLimitMessage, ct).ConfigureAwait(false);
            }

            return;
        }

        // Start thinking indicator (best-effort, fire-and-forget style).
        var thinkingIndicator = channel as IThinkingIndicator;
        try
        {
            if (thinkingIndicator is not null)
            {
                await thinkingIndicator.StartThinkingAsync(inbound.SenderId, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            /* best-effort — never let indicator errors affect the agent loop */
        }

        try
        {
            var session = await _handlers.LoadSession.HandleAsync(new LoadSession.Query(sessionId), ct).ConfigureAwait(false);

            // Prune stale/excess messages before building LLM context.
            var pruning = _defaults.SessionPruning;
            await _handlers.PruneSession.HandleAsync(
                new PruneSession.Command(session, pruning?.MaxMessages, pruning?.MaxAgeDays), ct).ConfigureAwait(false);

            // Strip metadata sentinels (model role markers, ChatML delimiters, canary tokens)
            // from user input before any processing to prevent confusion or injection.
            if (inbound.Channel != ChannelName.Cli)
            {
                var stripped = PromptGuard.StripMetadataSentinels(inbound.Text);
                if (!ReferenceEquals(stripped, inbound.Text))
                {
                    inbound = inbound with { Text = stripped };
                }
            }

            // Slash command interception — handle before the LLM sees the message.
            var slashResult = SlashCommandRouter.TryHandle(inbound.Text, out var slashError, out var slashArg);
            if (slashError is not null)
            {
                if (channel is not null)
                {
                    await channel.SendAsync(outbound with { Text = slashError }, ct).ConfigureAwait(false);
                }

                return;
            }

            if (slashResult.HasValue)
            {
                var reply = await HandleSlashCommandAsync(slashResult.Value, session, slashArg, ct).ConfigureAwait(false);
                if (reply is not null && channel is not null)
                {
                    await channel.SendAsync(outbound with { Text = reply }, ct).ConfigureAwait(false);
                }

                return;
            }

            // --- Stage 1: Build request context (memory, workspace, goals, tools, system prompt) ---
            var memoryContext = await _handlers.GetMemoryContext.HandleAsync(
                new GetMemoryContext.Query(inbound.Text), ct).ConfigureAwait(false);
            var reqCtxResult = await _handlers.BuildChatRequest.HandleAsync(
                new BuildChatRequest.Query(inbound, memoryContext), ct).ConfigureAwait(false);
            var reqCtx = reqCtxResult;

            // Inject canary token for exfiltration detection (enabled by default).
            var canaryEnabled = _appConfig.Security?.CanaryGuard?.Enabled ?? true;
            CanaryGuard? canaryGuard = null;
            if (canaryEnabled)
            {
                canaryGuard = new CanaryGuard();
            }

            var systemPrompt = reqCtx.SystemPrompt;
            if (canaryGuard is not null)
            {
                systemPrompt += canaryGuard.GenerateCanary();
            }

            // Assemble conversation messages with system prompt + session history.
            var messages = new List<ChatMessage>
            {
                new(MessageRole.System, systemPrompt)
            };

            if (!reqCtx.ContextWindowEnabled)
            {
                if (session.Messages.Count > _defaults.MaxContextMessages)
                {
                    messages.AddRange(session.Messages.GetRange(
                        session.Messages.Count - _defaults.MaxContextMessages, _defaults.MaxContextMessages));
                }
                else
                {
                    messages.AddRange(session.Messages);
                }
            }
            else
            {
                messages.AddRange(session.Messages);
            }

            // --- Stage 2: Context window guard (trimming / compaction) ---
            messages = await ApplyContextWindowGuardAsync(session, reqCtx, messages, ct).ConfigureAwait(false);

            // --- Stage 3: Security guards (prompt injection, vision drop) ---
            var secHandlerResult = await _handlers.ApplySecurityGuards.HandleAsync(
                new ApplySecurityGuards.Command(inbound, _provider.SupportsVision, _provider.Name), ct).ConfigureAwait(false);
            if (secHandlerResult.Blocked)
            {
                if (channel is not null)
                {
                    await channel.SendAsync(
                        outbound with { Text = "Message blocked: potential prompt injection detected." }, ct).ConfigureAwait(false);
                }

                return;
            }

            messages.Add(new ChatMessage(MessageRole.User, secHandlerResult.UserText, Images: secHandlerResult.Images));

            // --- Stage 4: Dispatch to provider (budget check, LLM call, cost recording) ---
            var dispatchResult = await DispatchToProviderAsync(
                sessionId, session, reqCtx, messages, outbound, channel, inbound, ct).ConfigureAwait(false);
            if (dispatchResult is null)
            {
                // Budget exceeded — rejection already sent to channel inside DispatchToProviderAsync.
                return;
            }

            // --- Stage 5: Post-process reply (leak detection, canary check, session save, send, consolidation) ---
            await PostProcessReplyAsync(
                inbound, session, dispatchResult, reqCtx.ContextWindowEnabled, outbound, channel, canaryGuard, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUnhandledError(_logger, ex, sessionId);
            try
            {
                if (channel is not null)
                {
                    await channel.SendAsync(
                        outbound with { Text = "Sorry, something went wrong. Please try again." }, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                /* best-effort notification — ignore secondary failures */
            }
        }
        finally
        {
            // Stop thinking indicator (best-effort).
            try
            {
                if (thinkingIndicator is not null)
                {
                    await thinkingIndicator.StopThinkingAsync(inbound.SenderId, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                /* best-effort — never let indicator errors affect the agent loop */
            }
        }
    }

    /// <summary>
    ///     Standard (non-streaming) tool loop. Delegates to ChatAsync on every iteration.
    ///     Returns the final text reply, or null if the iteration cap was hit.
    ///     When <paramref name="providerOverride"/> is non-null, it is used as the sole
    ///     candidate instead of the configured fallback chain.
    /// </summary>
    private async Task<LoopResult> RunNonStreamingLoopAsync(
        ChatRequest request,
        List<ChatMessage> messages,
        Session session,
        CancellationToken ct,
        IProvider? providerOverride = null)
    {
        IReadOnlyList<(string Name, IProvider Provider)> candidates;
        if (providerOverride is not null)
        {
            candidates = [(providerOverride.Name, providerOverride)];
        }
        else
        {
            candidates = GetFallbackCandidates();
        }
        long totalCacheRead = 0;
        long totalCacheWrite = 0;
        string? lastThinking = null;
        List<ToolCallSummary>? toolCallSummaries = null;
        var completedIterations = 0;

        for (var iteration = 0; iteration < _defaults.MaxToolIterations; iteration++)
        {
            ChatResponse response;
            try
            {
                response = await _fallbackChain.ExecuteAsync(
                    candidates,
                    (name, provider, token) => provider.ChatAsync(ApplyModelOverride(name, request), token),
                    ct);
            }
            catch (FallbackExhaustedException ex)
            {
                LogAllProvidersExhausted(ex.Message);
                return new LoopResult("Sorry, all configured providers are currently unavailable. Please try again later.", totalCacheRead,
                    totalCacheWrite, lastThinking, toolCallSummaries, completedIterations);
            }
            catch (Exception ex)
            {
                LogProviderError(_logger, ex);
                return new LoopResult("Sorry, I encountered an error processing your request.", totalCacheRead, totalCacheWrite,
                    lastThinking, toolCallSummaries, completedIterations);
            }

            session.TotalInputTokens += response.InputTokens ?? 0;
            session.TotalOutputTokens += response.OutputTokens ?? 0;
            totalCacheRead += response.CacheReadTokens ?? 0;
            totalCacheWrite += response.CacheWriteTokens ?? 0;
            lastThinking = response.ReasoningContent ?? lastThinking;

            if (response.ToolCalls?.Count > 0)
            {
                completedIterations++;
                toolCallSummaries ??= [];
                foreach (var tc in response.ToolCalls)
                {
                    toolCallSummaries.Add(new ToolCallSummary { Name = tc.Name, ResultLength = tc.ArgumentsJson.Length });
                }

                messages.Add(new ChatMessage(MessageRole.Assistant, response.Content, ToolCalls: response.ToolCalls));
                await ExecuteToolCallsAsync(response.ToolCalls, messages, ct);

                request = request with { Messages = messages };
                continue;
            }

            var finalReply = response.Content ?? "(no response)";
            if (session.ShowThinking && response.ReasoningContent is { Length: > 0 } thinking)
            {
                finalReply = $"<thinking>\n{thinking}\n</thinking>\n\n{finalReply}";
            }

            messages.Add(new ChatMessage(MessageRole.Assistant, finalReply));
            return new LoopResult(finalReply, totalCacheRead, totalCacheWrite, lastThinking, toolCallSummaries, completedIterations);
        }

        return new LoopResult(null, totalCacheRead, totalCacheWrite, lastThinking, toolCallSummaries, completedIterations); // iteration cap hit
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Fallback candidate management
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Builds the ordered candidate list for the fallback chain: primary provider first,
    ///     then each configured fallback provider. Built once and cached.
    /// </summary>
    private IReadOnlyList<(string Name, IProvider Provider)> GetFallbackCandidates()
    {
        if (_fallbackCandidates is not null)
        {
            return _fallbackCandidates;
        }

        var candidates = new List<(string Name, IProvider Provider)>
        {
            (_defaults.Provider, _provider)
        };

        var modelOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_defaults is { EnableProviderFallback: true, FallbackModels: { Count: > 0 } fallbacks })
        {
            foreach (var entry in fallbacks)
            {
                var fallbackName = entry.Provider;

                // Skip if it's the same as the primary (already added)
                if (string.Equals(fallbackName, _defaults.Provider, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var fallbackProvider = ProviderFactory.Create(
                        fallbackName,
                        _appConfig.Providers,
                        _httpClientFactory,
                        deviceFlow: null,
                        apiKeyOverride: entry.ApiKey,
                        baseUrlOverride: entry.BaseUrl,
                        authHeaderOverride: entry.AuthHeader);
                    candidates.Add((fallbackName, fallbackProvider));

                    if (entry.Model is not null)
                    {
                        modelOverrides[fallbackName] = entry.Model;
                    }

                    LogFallbackProviderRegistered(fallbackName);
                }
                catch (Exception ex)
                {
                    LogFallbackProviderCreateFailed(fallbackName, ex.Message);
                }
            }
        }

        _fallbackModelOverrides = modelOverrides;
        _fallbackCandidates = candidates;
        _streamingFallbackCandidates = candidates
                                       .Where(c => c.Provider is IStreamingProvider)
                                       .Select(c => (c.Name, (IStreamingProvider)c.Provider))
                                       .ToList();
        return _fallbackCandidates;
    }

    /// <summary>
    ///     Builds the ordered candidate list filtered to streaming providers only.
    /// </summary>
    private IReadOnlyList<(string Name, IStreamingProvider Provider)> GetStreamingFallbackCandidates()
    {
        if (_streamingFallbackCandidates is not null)
        {
            return _streamingFallbackCandidates;
        }

        GetFallbackCandidates();
        return _streamingFallbackCandidates!;
    }

    /// <summary>
    ///     Applies per-fallback model override to a chat request if one is configured for
    ///     the given candidate name. Returns the original request if no override exists.
    /// </summary>
    private ChatRequest ApplyModelOverride(string candidateName, ChatRequest request)
    {
        if (_fallbackModelOverrides is not null
            && _fallbackModelOverrides.TryGetValue(candidateName, out var modelOverride))
        {
            return request with { Model = modelOverride };
        }

        return request;
    }

    /// <summary>
    ///     Merges consecutive messages with the same role to prevent provider rejections.
    ///     Some providers (Anthropic, some OpenAI-compat) reject consecutive user/user or
    ///     assistant/assistant messages. This merges adjacent same-role messages by concatenating
    ///     their content with double-newline separators.
    ///     System messages are never merged. Tool messages are not merged (they have ToolCallIds).
    /// </summary>
    internal static List<ChatMessage> MergeConsecutiveRoles(List<ChatMessage> messages)
    {
        if (messages.Count <= 1)
        {
            return messages;
        }

        var result = new List<ChatMessage>(messages.Count);
        result.Add(messages[0]);

        for (var i = 1; i < messages.Count; i++)
        {
            var current = messages[i];
            var previous = result[^1];

            // Only merge user<->user or assistant<->assistant (not system, not tool)
            if (current.Role == previous.Role
                && current.Role != MessageRole.System
                && current.Role != MessageRole.Tool
                && current.ToolCalls is null // don't merge assistant messages that have tool calls
                && previous.ToolCalls is null)
            {
                var merged = (previous.Content ?? "") + "\n\n" + (current.Content ?? "");
                result[^1] = previous with { Content = merged.Trim() };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  LoggerMessage declarations
    // ──────────────────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Processing {SessionId} (queued {LagMs}ms ago)")]
    private static partial void LogProcessingMessage(ILogger logger, string sessionId, double lagMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Rate-limited session {SessionId}")]
    private static partial void LogRateLimited(ILogger logger, string sessionId);

    [LoggerMessage(EventId = 27, Level = LogLevel.Warning, Message = "IP rate-limited {IpAddress}")]
    private static partial void LogIpRateLimited(ILogger logger, string ipAddress);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Unhandled error for session {SessionId}")]
    private static partial void LogUnhandledError(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Provider error")]
    private static partial void LogProviderError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Tool: {ToolName} {ToolArgs}")]
    private static partial void LogToolExecution(ILogger logger, string toolName, string toolArgs);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Streaming provider error")]
    private static partial void LogStreamingProviderError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Streaming channel error")]
    private static partial void LogStreamingChannelError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Could not read {Path}")]
    private static partial void LogCouldNotReadSystemMd(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "Memory consolidation error")]
    private static partial void LogMemoryConsolidationError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
        Message = "Potential prompt injection detected in {Source}: matched pattern '{Pattern}'")]
    private partial void LogPotentialInjection(string source, string pattern);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "Dropped {ImageCount} image(s) from {Channel}: provider '{ProviderName}' does not support vision")]
    private static partial void LogImageDropped(ILogger logger, string channel, int imageCount, string providerName);

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug,
        Message = "Fallback provider '{ProviderName}' registered")]
    private partial void LogFallbackProviderRegistered(string providerName);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning,
        Message = "Could not create fallback provider '{ProviderName}': {ErrorMessage}")]
    private partial void LogFallbackProviderCreateFailed(string providerName, string errorMessage);

    [LoggerMessage(EventId = 15, Level = LogLevel.Error,
        Message = "All providers exhausted: {ErrorMessage}")]
    private partial void LogAllProvidersExhausted(string errorMessage);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning,
        Message = "Context window emergency: {Estimated}/{Window} tokens. Force-compressing.")]
    private partial void LogContextWindowEmergency(int estimated, int window);

    [LoggerMessage(EventId = 17, Level = LogLevel.Information,
        Message = "Context window at {Estimated}/{Window} tokens. Compacting.")]
    private partial void LogContextWindowCompacting(int estimated, int window);

    [LoggerMessage(EventId = 18, Level = LogLevel.Information,
        Message = "Pre-compaction memory flush: saved {Chars} chars from {Count} messages.")]
    private partial void LogPreCompactionFlushComplete(int count, int chars);

    [LoggerMessage(EventId = 19, Level = LogLevel.Warning,
        Message = "Pre-compaction memory flush failed.")]
    private partial void LogPreCompactionFlushFailed(Exception exception);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning,
        Message = "Request blocked for {SessionId}: {Reason}")]
    private partial void LogBudgetExceededReject(string sessionId, string reason);

    [LoggerMessage(EventId = 21, Level = LogLevel.Warning,
        Message = "Failed to save session {SessionId} after LLM response — reply will still be delivered")]
    private partial void LogSessionSaveFailed(Exception exception, string sessionId);

    [LoggerMessage(EventId = 22, Level = LogLevel.Warning,
        Message = "Scrubbed {Count} secret pattern(s) from extracted facts before memory storage")]
    private partial void LogFactSecretsScrubbed(int count);

    [LoggerMessage(EventId = 23, Level = LogLevel.Critical,
        Message = "CANARY EXFILTRATION: LLM leaked system prompt content to {Channel}:{SenderId}")]
    private partial void LogCanaryExfiltrationDetected(string channel, string senderId);

    [LoggerMessage(EventId = 24, Level = LogLevel.Debug,
        Message = "Model routing: {SessionId} score={Score} (threshold={Threshold}) -> simple model {Model}")]
    private partial void LogModelRoutingSimple(string sessionId, int score, int threshold, string model);

    [LoggerMessage(EventId = 25, Level = LogLevel.Debug,
        Message = "Model routing: {SessionId} score={Score} (threshold={Threshold}) -> primary model {Model}")]
    private partial void LogModelRoutingPrimary(string sessionId, int score, int threshold, string model);

    [LoggerMessage(EventId = 26, Level = LogLevel.Warning,
        Message = "Failed to deliver pending file {Filename}: {Error}")]
    private static partial void LogPendingFileDeliveryFailed(ILogger logger, string filename, string error);

    [LoggerMessage(EventId = 27, Level = LogLevel.Warning,
        Message = "Scrubbed {Count} secret pattern(s) from memory consolidation summary")]
    private partial void LogConsolidationSecretsScrubbed(int count);
}