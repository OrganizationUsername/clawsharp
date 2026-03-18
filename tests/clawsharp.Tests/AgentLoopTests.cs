using clawsharp;
using Clawsharp.Analytics;
using Clawsharp.Channels;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Cost;
using Clawsharp.Goals;
using Clawsharp.Memory;
using Clawsharp.Providers;
using Clawsharp.Security;
using Clawsharp.Tests.Fakes;
using Clawsharp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests;

/// <summary>
/// Test harness that wires up a complete AgentLoop with fakes for integration testing.
/// Each test should create its own harness instance with using/Dispose.
/// </summary>
internal sealed class AgentLoopTestHarness : IDisposable
{
    private readonly string _workspaceDir;

    private readonly string _sessionsDir;

    private readonly string _costFilePath;

    private readonly AgentLoop _loop;

    private readonly SessionStore _sessionManager;

    public FakeStreamingProvider Provider { get; }

    public FakeStreamingChannel Channel { get; }

    public FakeToolRegistry Tools { get; }

    public FakeMemory Memory { get; }

    public AgentDefaults Defaults { get; }

    public SessionStore Sessions => _sessionManager;

    public IInteractionStore InteractionStore { get; }

    public AgentLoopTestHarness(
        AgentDefaults? overrideDefaults = null,
        AnalyticsConfig? analyticsConfig = null,
        IInteractionStore? interactionStore = null)
    {
        _workspaceDir = Path.Combine(Path.GetTempPath(), "clawsharp-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspaceDir);

        Provider = new FakeStreamingProvider();
        Channel = new FakeStreamingChannel();
        Tools = new FakeToolRegistry();
        Memory = new FakeMemory();

        var d = overrideDefaults;
        Defaults = new AgentDefaults
        {
            Provider = d?.Provider ?? "fake-streaming",
            Model = d?.Model ?? "test-model",
            ConsolidateEvery = d?.ConsolidateEvery ?? 0,
            RateLimitRequests = d?.RateLimitRequests ?? 100,
            RateLimitWindow = d?.RateLimitWindow ?? TimeSpan.FromSeconds(60),
            PromptInjectionGuard = d?.PromptInjectionGuard ?? false,
            MaxToolIterations = d?.MaxToolIterations ?? 5,
            MaxContextMessages = d?.MaxContextMessages ?? 200,
            Temperature = d?.Temperature ?? 0.7f,
            HealthCheck = d?.HealthCheck,
        };

        var appConfig = new AppConfig
        {
            Tools = new ToolsConfig { Workspace = _workspaceDir },
            Analytics = analyticsConfig,
        };

        var defaultsOptions = Options.Create(Defaults);
        var appConfigOptions = Options.Create(appConfig);

        // Use temp directory for sessions to avoid polluting ~/.clawsharp/sessions/.
        _sessionsDir = Path.Combine(_workspaceDir, "sessions");
        _sessionManager = new SessionStore(_sessionsDir, NullLogger<SessionStore>.Instance);
        var sessionManager = _sessionManager;

        // CostTracker with tracking disabled
        _costFilePath = Path.Combine(_workspaceDir, "costs.jsonl");
        var costStorage = new CostStorage(_costFilePath);
        var costConfig = new CostConfig { Enabled = false };
        var costTracker = new CostTracker(costStorage, Options.Create(costConfig), NullLogger<CostTracker>.Instance);

        // CompactionService
        var compactionService = new CompactionService(costTracker, null, NullLogger<CompactionService>.Instance);

        // RateLimiter
        var rateLimiter = new RateLimiter(defaultsOptions);

        // FallbackChain
        var cooldownTracker = new CooldownTracker();
        var fallbackChain = new FallbackChain(cooldownTracker, NullLogger<FallbackChain>.Instance);

        // Create the channel list with our named fake channel
        var channels = new List<IChannel> { new NamedFakeStreamingChannel(ChannelName.Telegram, Channel) };

        // GoalStorage — use temp path to avoid touching real user directory
        var goalStorage = new GoalStorage(
            Path.Combine(_workspaceDir, "goals.json"),
            NullLogger<GoalStorage>.Instance);

        var auditLogger = new AuditLogger(appConfigOptions, NullLogger<AuditLogger>.Instance);
        var factExtractor = new FactExtractor();

        // Build a ServiceProvider that registers all handler dependencies + handlers
        // so we can resolve the generated Handler types for the AgentLoop constructor.
        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddLogging(b => b.ClearProviders());
        services.AddSingleton<IProvider>(Provider);
        services.AddSingleton<IMemory>(Memory);
        services.AddSingleton<IToolRegistry>(Tools);
        services.AddSingleton(sessionManager);
        services.AddSingleton(compactionService);
        services.AddSingleton(costTracker);
        services.AddSingleton(defaultsOptions);
        services.AddSingleton(appConfigOptions);
        services.AddSingleton(auditLogger);
        services.AddSingleton(goalStorage);
        services.AddSingleton(factExtractor);
        services.AddclawsharpBehaviors();
        services.AddclawsharpHandlers(ServiceLifetime.Singleton);
        services.AddSingleton<AgentHandlers>();
        var sp = services.BuildServiceProvider();

        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        InteractionStore = interactionStore ?? new InteractionStorage(
            Path.Combine(Path.GetTempPath(), $"clawsharp-test-{Guid.NewGuid():N}", "interactions.jsonl"));

        var interactionTracker = new InteractionTracker(
            InteractionStore, Memory, NullLogger<InteractionTracker>.Instance,
            storeInMemory: analyticsConfig?.StoreInMemory ?? false);

        _loop = new AgentLoop(
            provider: Provider,
            channels: channels,
            tools: Tools,
            memory: Memory,
            sessions: sessionManager,
            compactionService: compactionService,
            costTracker: costTracker,
            defaultsOptions: defaultsOptions,
            configOptions: appConfigOptions,
            rateLimiter: rateLimiter,
            fallbackChain: fallbackChain,
            httpClientFactory: httpClientFactory,
            logger: NullLogger<AgentLoop>.Instance,
            auditLogger: auditLogger,
            goalStorage: goalStorage,
            factExtractor: factExtractor,
            interactionTracker: interactionTracker,
            handlers: sp.GetRequiredService<AgentHandlers>());
    }

    /// <summary>
    /// Process a message through the agent loop and return the resulting session.
    /// </summary>
    public async Task<Session> ProcessAsync(string text, string? senderId = null, ChannelName? channel = null)
    {
        senderId ??= GetUniqueSenderId();
        channel ??= ChannelName.Telegram;

        var inbound = new InboundMessage(
            Channel: channel,
            SenderId: senderId,
            SenderName: "Test User",
            Text: text,
            ArrivedAt: DateTimeOffset.UtcNow);

        await _loop.ProcessMessageAsync(inbound, CancellationToken.None);

        // Load the session from the temp-directory-backed manager to return it
        return await _sessionManager.LoadOrCreateAsync($"{channel.Value}:{senderId}");
    }

    /// <summary>
    /// Process a message with full InboundMessage control.
    /// </summary>
    public async Task<Session> ProcessRawAsync(InboundMessage inbound)
    {
        await _loop.ProcessMessageAsync(inbound, CancellationToken.None);
        return await _sessionManager.LoadOrCreateAsync($"{inbound.Channel.Value}:{inbound.SenderId}");
    }

    public static string GetUniqueSenderId() => "test-" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspaceDir))
            {
                Directory.Delete(_workspaceDir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

/// <summary>
/// Wraps FakeStreamingChannel but with a configurable Name,
/// so AgentLoop can find the channel by name matching inbound.Channel.
/// </summary>
internal sealed class NamedFakeStreamingChannel(ChannelName name, FakeStreamingChannel inner) : IStreamingChannel
{
    public ChannelName Name => name;

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        return inner.SendAsync(message, ct);
    }

    public Task StreamAsync(OutboundMessage message, IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        return inner.StreamAsync(message, tokens, ct);
    }
}

[TestFixture]
public sealed class AgentLoopTests
{
    // ── 1. Simple text streaming ──

    [Test]
    public async Task ProcessMessage_SimpleText_StreamsResponseAndSavesSession()
    {
        using var harness = new AgentLoopTestHarness();
        harness.Provider.EnqueueTextStream("Hello", " ", "World");

        var senderId = AgentLoopTestHarness.GetUniqueSenderId();
        var session = await harness.ProcessAsync("Hi there", senderId);

        // The streaming channel should have received tokens
        harness.Channel.StreamedTokenBatches.ShouldNotBeEmpty();
        var tokens = harness.Channel.StreamedTokenBatches[0];
        string.Join("", tokens).ShouldBe("Hello World");

        // Session should have the history saved
        session.Messages.ShouldNotBeEmpty();
        session.Messages.Count.ShouldBe(2); // user + assistant
    }

    // ── 2. Two turns accumulate history ──

    [Test]
    public async Task ProcessMessage_TwoTurns_SessionAccumulatesHistory()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Provider.EnqueueTextStream("Response 1");
        await harness.ProcessAsync("Turn 1", senderId);

        harness.Provider.EnqueueTextStream("Response 2");
        await harness.ProcessAsync("Turn 2", senderId);

        // Second request should contain history from the first turn plus the new user message.
        // The provider receives: [system, history..., user] — verify second request has more messages.
        harness.Provider.ReceivedRequests.Count.ShouldBe(2);

        // First request: system + user = 2 messages
        var firstCount = harness.Provider.ReceivedRequests[0].Messages.Count;
        // Second request: system + (user1 + assistant1 from session history) + user2 = at least 4
        var secondCount = harness.Provider.ReceivedRequests[1].Messages.Count;
        secondCount.ShouldBeGreaterThan(firstCount);

        // Two streaming batches were delivered
        harness.Channel.StreamedTokenBatches.Count.ShouldBe(2);
    }

    // ── 3. Second turn includes history in request ──

    [Test]
    public async Task ProcessMessage_SecondTurn_IncludesHistoryInRequest()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Provider.EnqueueTextStream("First response");
        await harness.ProcessAsync("First message", senderId);

        var firstRequestMessageCount = harness.Provider.ReceivedRequests[0].Messages.Count;

        harness.Provider.EnqueueTextStream("Second response");
        await harness.ProcessAsync("Second message", senderId);

        var secondRequestMessageCount = harness.Provider.ReceivedRequests[1].Messages.Count;

        // Second request should have more messages (includes history from turn 1)
        secondRequestMessageCount.ShouldBeGreaterThan(firstRequestMessageCount);
    }

    // ── 4. Tool call executes tool and continues ──

    [Test]
    public async Task ProcessMessage_ToolCall_ExecutesToolAndContinues()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Tools.AddDefinition("shell", "run shell command", """{"type":"object","properties":{"command":{"type":"string"}}}""");
        harness.Tools.EnqueueResult("shell", "file1.txt\nfile2.txt");

        // First stream: tool call
        harness.Provider.EnqueueToolCallStream("call_1", "shell", """{"command":"ls"}""");
        // Second stream: final text response after tool result
        harness.Provider.EnqueueTextStream("I found two files.");

        await harness.ProcessAsync("List files", senderId);

        harness.Tools.ExecutedCalls.ShouldContain(c => c.Name == "shell");
        // The channel should have received the final text
        harness.Channel.StreamedTokenBatches.ShouldNotBeEmpty();
    }

    // ── 5. Multiple tool calls execute all before continuing ──

    [Test]
    public async Task ProcessMessage_MultipleToolCalls_ExecutesAllBeforeContinuing()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Tools.AddDefinition("tool_a", "tool A");
        harness.Tools.AddDefinition("tool_b", "tool B");

        // Stream with two tool calls at different indices
        harness.Provider.EnqueueChunks(
            new ToolCallChunk(0, "call_a", "tool_a", null),
            new ToolCallChunk(0, null, null, """{"x":1}"""),
            new ToolCallChunk(1, "call_b", "tool_b", null),
            new ToolCallChunk(1, null, null, """{"y":2}"""),
            new StreamDoneChunk());

        // Final response after both tool results
        harness.Provider.EnqueueTextStream("Both tools executed.");

        await harness.ProcessAsync("Run both tools", senderId);

        harness.Tools.ExecutedCalls.Count.ShouldBe(2);
        harness.Tools.ExecutedCalls.ShouldContain(c => c.Name == "tool_a");
        harness.Tools.ExecutedCalls.ShouldContain(c => c.Name == "tool_b");
    }

    // ── 6. Thinking shown prepends thinking block ──

    [Test]
    public async Task ProcessMessage_ThinkingShown_PrependsThinkingBlock()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Provider.EnqueueThinkingThenTextStream("Deep reasoning here", "The answer is 42");

        // Enable thinking on the session before processing so AgentLoop
        // picks up ShowThinking = true when it loads the session.
        var sessionId = $"telegram:{senderId}";
        var session = await harness.Sessions.LoadOrCreateAsync(sessionId);
        session.ShowThinking = true;
        await harness.Sessions.SaveAsync(session);

        await harness.ProcessAsync("Think hard", senderId);

        // When ShowThinking is on, the final reply stored in session includes <thinking>.
        // Verify via the session's last message content (deserialized from disk).
        var updatedSession = await harness.Sessions.LoadOrCreateAsync(sessionId);
        // Find the assistant message by content since Intellenum Role may not roundtrip via JSON.
        var assistantMsg = updatedSession.Messages.LastOrDefault(m =>
            m.Content is not null && m.Content.Contains("42"));
        assistantMsg.ShouldNotBeNull();
        assistantMsg!.Content.ShouldNotBeNull();
        assistantMsg.Content!.ShouldContain("<thinking>");
        assistantMsg.Content.ShouldContain("Deep reasoning here");
    }

    // ── 7. Thinking hidden does not include thinking block ──

    [Test]
    public async Task ProcessMessage_ThinkingHidden_OmitsThinkingBlock()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Provider.EnqueueThinkingThenTextStream("Secret reasoning", "The answer is 42");

        // ShowThinking defaults to false, so no pre-setup needed
        await harness.ProcessAsync("Think quietly", senderId);

        // The streamed text should NOT contain thinking tags
        harness.Channel.StreamedTokenBatches.ShouldNotBeEmpty();
        var allStreamedText = string.Join("", harness.Channel.StreamedTokenBatches[0]);
        allStreamedText.ShouldNotContain("<thinking>");

        // Also verify via the persisted session (content match, not role match)
        var sessionId = $"telegram:{senderId}";
        var updatedSession = await harness.Sessions.LoadOrCreateAsync(sessionId);
        var assistantMsg = updatedSession.Messages.LastOrDefault(m =>
            m.Content is not null && m.Content.Contains("42"));
        assistantMsg.ShouldNotBeNull();
        assistantMsg!.Content!.ShouldNotContain("<thinking>");
    }

    // ── 8. Rate limited rejects without calling provider ──

    [Test]
    public async Task ProcessMessage_RateLimited_RejectsWithoutCallingProvider()
    {
        using var harness = new AgentLoopTestHarness(new AgentDefaults
        {
            RateLimitRequests = 1,
            RateLimitWindow = TimeSpan.FromSeconds(60),
        });
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        // First message should succeed
        harness.Provider.EnqueueTextStream("First response");
        await harness.ProcessAsync("Message 1", senderId);

        // Second message should be rate-limited (no provider call)
        // Enqueue a response just in case, but it should not be consumed
        harness.Provider.EnqueueTextStream("Should not reach");
        await harness.ProcessAsync("Message 2", senderId);

        // Provider should have been called only once
        harness.Provider.ReceivedRequests.Count.ShouldBe(1);

        // Channel should have received the rate-limit message as a SendAsync (not streaming)
        harness.Channel.SentMessages.ShouldContain(m => m.Contains("too fast"));
    }

    // ── 9. Slash /clear clears session ──

    [Test]
    public async Task ProcessMessage_SlashClear_ClearsSession()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        // Process one message to create session with history
        harness.Provider.EnqueueTextStream("Hi there");
        await harness.ProcessAsync("Hello", senderId);

        // Now send /clear
        await harness.ProcessAsync("/clear", senderId);

        var session = await harness.Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        session.Messages.Count.ShouldBe(0);
    }

    // ── 10. Slash /status returns status without calling provider ──

    [Test]
    public async Task ProcessMessage_SlashStatus_ReturnsStatusWithoutCallingProvider()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        // Process /status — should not call provider at all
        await harness.ProcessAsync("/status", senderId);

        harness.Provider.ReceivedRequests.Count.ShouldBe(0);
        // Status message is sent via non-streaming SendAsync
        harness.Channel.SentMessages.ShouldNotBeEmpty();
        harness.Channel.SentMessages.ShouldContain(m => m.Contains("Model:"));
    }

    // ── 11. Max tool iterations returns cap message ──

    [Test]
    public async Task ProcessMessage_MaxToolIterations_ReturnsCapMessage()
    {
        using var harness = new AgentLoopTestHarness(new AgentDefaults { MaxToolIterations = 2 });
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Tools.AddDefinition("looper", "infinite loop tool");

        // Queue 2 tool call streams (hitting the iteration cap) — no final text stream after
        harness.Provider.EnqueueToolCallStream("call_1", "looper", """{"x":1}""");
        harness.Provider.EnqueueToolCallStream("call_2", "looper", """{"x":2}""");

        await harness.ProcessAsync("Loop forever", senderId);

        // The tool should have been called exactly MaxToolIterations times
        harness.Tools.ExecutedCalls.Count.ShouldBe(2);

        // When the iteration cap is hit, RunStreamingLoopAsync returns null which
        // becomes "(max tool iterations reached)". This is stored in the session.
        // Verify via disk — find by content since Intellenum Role may not roundtrip via JSON.
        var session = await harness.Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        var capMsg = session.Messages.LastOrDefault(m =>
            m.Content is not null && m.Content.Contains("max tool iterations"));
        capMsg.ShouldNotBeNull();
    }

    // ── 12. Heartbeat bypasses rate limit ──

    [Test]
    public async Task ProcessMessage_Heartbeat_BypassesRateLimit()
    {
        using var harness = new AgentLoopTestHarness(new AgentDefaults
        {
            RateLimitRequests = 1,
            RateLimitWindow = TimeSpan.FromSeconds(60),
        });
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        // First message (normal) — consumes the rate limit
        harness.Provider.EnqueueTextStream("Normal response");
        await harness.ProcessAsync("Normal message", senderId);

        // Heartbeat message should bypass rate limiting
        harness.Provider.EnqueueTextStream("Heartbeat response");
        var heartbeatInbound = new InboundMessage(
            Channel: ChannelName.Telegram,
            SenderId: senderId,
            SenderName: "Heartbeat",
            Text: "Heartbeat check",
            ArrivedAt: DateTimeOffset.UtcNow,
            IsHeartbeat: true);

        await harness.ProcessRawAsync(heartbeatInbound);

        // Provider should have been called twice (normal + heartbeat)
        harness.Provider.ReceivedRequests.Count.ShouldBe(2);
    }

    // ── 13. Prompt guard wraps user message ──

    [Test]
    public async Task ProcessMessage_PromptGuardEnabled_WrapsUserMessage()
    {
        using var harness = new AgentLoopTestHarness(new AgentDefaults { PromptInjectionGuard = true });
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        harness.Provider.EnqueueTextStream("Guarded response");
        await harness.ProcessAsync("Hello from non-CLI", senderId);

        // The user message sent to the provider should be wrapped in XML delimiters
        harness.Provider.ReceivedRequests.Count.ShouldBe(1);
        var messages = harness.Provider.ReceivedRequests[0].Messages;
        var lastUserMsg = messages.LastOrDefault(m => m.Role == MessageRole.User);
        lastUserMsg.ShouldNotBeNull();
        lastUserMsg!.Content.ShouldNotBeNull();
        lastUserMsg.Content!.ShouldContain("<user_message>");
    }

    // ── 14. Provider throws sends error to channel ──

    [Test]
    public async Task ProcessMessage_ProviderThrows_SendsErrorToChannel()
    {
        using var harness = new AgentLoopTestHarness();
        var senderId = AgentLoopTestHarness.GetUniqueSenderId();

        // Don't enqueue any responses — FakeStreamingProvider throws with no chunks queued.
        // FallbackChain catches the retriable exception, exhausts all candidates, and raises
        // FallbackExhaustedException. The streaming loop catches that and returns the
        // provider-unavailable message. The key invariant is that no exception escapes
        // ProcessMessageAsync.
        await harness.ProcessAsync("This will fail", senderId);

        // Verify graceful handling: session should be written with at least user + one reply.
        var session = await harness.Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        session.Messages.Count.ShouldBeGreaterThanOrEqualTo(2);

        // The last message is one of the graceful fallback replies — exact wording depends on
        // whether the FallbackChain exhausted candidates (provider-unavailable) or the stream
        // completed empty ((no response)).
        var lastMsg = session.Messages.Last();
        lastMsg.Content.ShouldNotBeNull();
        lastMsg.Content!.ShouldNotBeEmpty();
    }
}