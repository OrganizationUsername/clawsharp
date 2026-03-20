using System.Net;
using clawsharp;
using Clawsharp.Analytics;
using Clawsharp.Channels;
using Clawsharp.Config;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Cost;
using Clawsharp.Goals;
using Clawsharp.Memory;
using Clawsharp.Providers;
using Clawsharp.Providers.OpenRouter;
using Clawsharp.Security;
using Clawsharp.Tests.Fakes;
using Clawsharp.Tests.Unit.Providers;
using Clawsharp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Unit.Pipeline;

/// <summary>
/// Configurable test harness for slash command handler testing.
/// Extends the basic AgentLoopTestHarness concept with support for
/// cost config, context window config, and custom provider injection.
/// </summary>
internal sealed class SlashCommandHarness : IDisposable
{
    private readonly string _workspaceDir;
    private readonly AgentLoop _loop;

    public FakeStreamingProvider Provider { get; }
    public FakeStreamingChannel Channel { get; }
    public FakeMemory Memory { get; }
    public SessionStore Sessions { get; }
    public GoalStorage GoalStorage { get; }
    public AgentDefaults Defaults { get; }
    public AppConfig AppConfig { get; }

    /// <summary>
    /// When non-null, this provider is used instead of FakeStreamingProvider.
    /// Enables OpenRouter-specific slash command testing.
    /// </summary>
    public IProvider? ProviderOverride { get; }

    public SlashCommandHarness(
        AgentDefaults? defaults = null,
        CostConfig? costConfig = null,
        ContextWindowConfig? contextWindow = null,
        IProvider? providerOverride = null)
    {
        _workspaceDir = Path.Combine(Path.GetTempPath(), "clawsharp-slash-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspaceDir);

        Provider = new FakeStreamingProvider();
        Channel = new FakeStreamingChannel();
        Memory = new FakeMemory();
        ProviderOverride = providerOverride;

        var d = defaults;
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
            ContextWindow = contextWindow ?? d?.ContextWindow,
            Compaction = d?.Compaction,
        };

        AppConfig = new AppConfig
        {
            Tools = new ToolsConfig { Workspace = _workspaceDir },
            Cost = costConfig,
        };

        var defaultsOptions = Options.Create(Defaults);
        var appConfigOptions = Options.Create(AppConfig);

        var sessionsDir = Path.Combine(_workspaceDir, "sessions");
        Sessions = new SessionStore(sessionsDir, NullLogger<SessionStore>.Instance);

        var costFilePath = Path.Combine(_workspaceDir, "costs.jsonl");
        var costStorage = new CostStorage(costFilePath);
        var effectiveCostConfig = costConfig ?? new CostConfig { Enabled = false };
        var costTracker = new CostTracker(costStorage, Options.Create(effectiveCostConfig), NullLogger<CostTracker>.Instance);

        var compactionService = new CompactionService(costTracker, null, NullLogger<CompactionService>.Instance);
        var rateLimiter = new RateLimiter(defaultsOptions);
        var cooldownTracker = new CooldownTracker();
        var fallbackChain = new FallbackChain(cooldownTracker, NullLogger<FallbackChain>.Instance);

        var channels = new List<IChannel> { new NamedFakeStreamingChannel(ChannelName.Telegram, Channel) };

        GoalStorage = new GoalStorage(
            Path.Combine(_workspaceDir, "goals.json"),
            NullLogger<GoalStorage>.Instance);

        var auditLogger = new AuditLogger(appConfigOptions, NullLogger<AuditLogger>.Instance);
        var factExtractor = new FactExtractor();

        var effectiveProvider = (IProvider?)providerOverride ?? Provider;

        var services = new ServiceCollection();
        services.AddHttpClient("llm");
        services.AddLogging(b => b.ClearProviders());
        services.AddSingleton(effectiveProvider);
        services.AddSingleton<IMemory>(Memory);
        services.AddSingleton<IToolRegistry>(new FakeToolRegistry());
        services.AddSingleton(Sessions);
        services.AddSingleton(compactionService);
        services.AddSingleton(costTracker);
        services.AddSingleton(defaultsOptions);
        services.AddSingleton(appConfigOptions);
        services.AddSingleton(auditLogger);
        services.AddSingleton(GoalStorage);
        services.AddSingleton(factExtractor);
        services.AddclawsharpBehaviors();
        services.AddclawsharpHandlers(ServiceLifetime.Singleton);
        services.AddSingleton<AgentHandlers>();
        var sp = services.BuildServiceProvider();

        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        var interactionStore = new InteractionStorage(
            Path.Combine(_workspaceDir, "interactions.jsonl"));
        var interactionTracker = new InteractionTracker(
            interactionStore, Memory, NullLogger<InteractionTracker>.Instance, storeInMemory: false);

        _loop = new AgentLoop(
            provider: effectiveProvider,
            channels: channels,
            tools: new FakeToolRegistry(),
            memory: Memory,
            sessions: Sessions,
            compactionService: compactionService,
            costTracker: costTracker,
            defaultsOptions: defaultsOptions,
            configOptions: appConfigOptions,
            rateLimiter: rateLimiter,
            fallbackChain: fallbackChain,
            httpClientFactory: httpClientFactory,
            logger: NullLogger<AgentLoop>.Instance,
            auditLogger: auditLogger,
            goalStorage: GoalStorage,
            factExtractor: factExtractor,
            interactionTracker: interactionTracker,
            handlers: sp.GetRequiredService<AgentHandlers>());
    }

    /// <summary>
    /// Process a slash command and return the text sent back to the channel.
    /// </summary>
    public async Task<string?> SendCommandAsync(string text, string? senderId = null)
    {
        senderId ??= "slash-test-" + Guid.NewGuid().ToString("N")[..8];

        var inbound = new InboundMessage(
            Channel: ChannelName.Telegram,
            SenderId: senderId,
            SenderName: "Test User",
            Text: text,
            ArrivedAt: DateTimeOffset.UtcNow);

        Channel.SentMessages.Clear();
        await _loop.ProcessMessageAsync(inbound, CancellationToken.None);

        // Slash commands respond via SendAsync (non-streaming), so check SentMessages
        return Channel.SentMessages.Count > 0 ? Channel.SentMessages[^1] : null;
    }

    /// <summary>
    /// Process a slash command using a specific sender, preserving session state.
    /// </summary>
    public async Task<(string? Reply, Session Session)> SendCommandWithSessionAsync(
        string text, string senderId)
    {
        var inbound = new InboundMessage(
            Channel: ChannelName.Telegram,
            SenderId: senderId,
            SenderName: "Test User",
            Text: text,
            ArrivedAt: DateTimeOffset.UtcNow);

        Channel.SentMessages.Clear();
        await _loop.ProcessMessageAsync(inbound, CancellationToken.None);

        var session = await Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        var reply = Channel.SentMessages.Count > 0 ? Channel.SentMessages[^1] : null;
        return (reply, session);
    }

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

// ════════════════════════════════════════════════════════════════════════
//  /clear and /new — Session clearing
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashClearHandlerTests
{
    [Test]
    public async Task Clear_ReturnsSessionClearedMessage()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/clear");
        reply.ShouldBe("Session cleared.");
    }

    [Test]
    public async Task New_ReturnsSessionClearedMessage()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/new");
        reply.ShouldBe("Session cleared.");
    }

    [Test]
    public async Task Clear_ActuallyClearsSessionMessages()
    {
        using var h = new SlashCommandHarness();
        var senderId = "clear-test-" + Guid.NewGuid().ToString("N")[..8];

        // Build up some session history first
        h.Provider.EnqueueTextStream("Hello back");
        var inbound = new InboundMessage(
            Channel: ChannelName.Telegram, SenderId: senderId,
            SenderName: "Test", Text: "Hello",
            ArrivedAt: DateTimeOffset.UtcNow);
        await h.SendCommandWithSessionAsync("Hello is not a slash command but we need session state", senderId);
        // The above won't add messages since it's not a slash command and the provider gets called.
        // Instead, manually pre-populate session via SessionStore.
        var session = new Session { Id = $"telegram:{senderId}" };
        session.Messages.Add(new ChatMessage(MessageRole.User, "Hello"));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, "Hi there"));
        session.TotalInputTokens = 500;
        session.TotalOutputTokens = 200;
        await h.Sessions.SaveAsync(session);

        // Verify messages exist before clear
        var before = await h.Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        before.Messages.Count.ShouldBe(2);

        // Send /clear
        var (reply, after) = await h.SendCommandWithSessionAsync("/clear", senderId);
        reply.ShouldBe("Session cleared.");
        after.Messages.Count.ShouldBe(0);

        // ClearSession only clears messages — token counters are preserved
        after.TotalInputTokens.ShouldBe(500);
        after.TotalOutputTokens.ShouldBe(200);
    }

    [Test]
    public async Task Clear_OnEmptySession_StillReturnsClearedMessage()
    {
        using var h = new SlashCommandHarness();
        var senderId = "empty-clear-" + Guid.NewGuid().ToString("N")[..8];
        var reply = await h.SendCommandAsync("/clear", senderId);
        reply.ShouldBe("Session cleared.");
    }

    [Test]
    public async Task Clear_DoesNotCallProvider()
    {
        using var h = new SlashCommandHarness();
        await h.SendCommandAsync("/clear");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /status — Status reporting
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashStatusHandlerTests
{
    [Test]
    public async Task Status_ContainsModelName()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "gpt-4o-mini" });
        var reply = await h.SendCommandAsync("/status");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model: gpt-4o-mini");
    }

    [Test]
    public async Task Status_ContainsMessageCount()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/status");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Messages: 0");
    }

    [Test]
    public async Task Status_ContainsTokenCounts()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/status");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Input tokens:");
        reply.ShouldContain("Output tokens:");
    }

    [Test]
    public async Task Status_ContainsThinkingStatus()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/status");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking: off");
    }

    [Test]
    public async Task Status_ShowsSessionOverride_WhenModelOverrideSet()
    {
        using var h = new SlashCommandHarness();
        var senderId = "status-override-" + Guid.NewGuid().ToString("N")[..8];

        // Set model override on session
        var session = new Session { Id = $"telegram:{senderId}" };
        session.ModelOverride = "claude-opus-4-0520";
        await h.Sessions.SaveAsync(session);

        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model: claude-opus-4-0520");
        reply.ShouldContain("(session override)");
    }

    [Test]
    public async Task Status_DoesNotShowSessionOverride_WhenNoOverride()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "default-model" });
        var reply = await h.SendCommandAsync("/status");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model: default-model");
        reply.ShouldNotContain("session override");
    }

    [Test]
    public async Task Status_ContainsMemoryLines()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/status");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Memory lines:");
    }

    [Test]
    public async Task Status_ReflectsAccumulatedTokens()
    {
        using var h = new SlashCommandHarness();
        var senderId = "tokens-" + Guid.NewGuid().ToString("N")[..8];

        // Pre-populate session with token counts
        var session = new Session { Id = $"telegram:{senderId}" };
        session.TotalInputTokens = 1234;
        session.TotalOutputTokens = 5678;
        await h.Sessions.SaveAsync(session);

        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Input tokens: 1,234");
        reply.ShouldContain("Output tokens: 5,678");
    }

    [Test]
    public async Task Status_ReflectsMessageCount()
    {
        using var h = new SlashCommandHarness();
        var senderId = "msg-count-" + Guid.NewGuid().ToString("N")[..8];

        var session = new Session { Id = $"telegram:{senderId}" };
        session.Messages.Add(new ChatMessage(MessageRole.User, "Hello"));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, "Hi"));
        session.Messages.Add(new ChatMessage(MessageRole.User, "How are you?"));
        await h.Sessions.SaveAsync(session);

        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Messages: 3");
    }

    [Test]
    public async Task Status_ShowsThinkingOn_WhenEnabled()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-status-" + Guid.NewGuid().ToString("N")[..8];

        var session = new Session { Id = $"telegram:{senderId}" };
        session.ShowThinking = true;
        await h.Sessions.SaveAsync(session);

        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking: on");
    }

    [Test]
    public async Task Status_ShowsTotalTurns()
    {
        using var h = new SlashCommandHarness();
        var senderId = "turns-" + Guid.NewGuid().ToString("N")[..8];

        var session = new Session { Id = $"telegram:{senderId}" };
        session.TotalMessageCount = 10; // 10 messages = 5 turns
        await h.Sessions.SaveAsync(session);

        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Total turns: 5");
    }

    [Test]
    public async Task Status_DoesNotCallProvider()
    {
        using var h = new SlashCommandHarness();
        await h.SendCommandAsync("/status");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /usage and /cost — Cost tracking
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashUsageHandlerTests
{
    [Test]
    public async Task Usage_WhenCostTrackingDisabled_ReturnsNotEnabledMessage()
    {
        using var h = new SlashCommandHarness(costConfig: null);
        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Cost tracking is not enabled");
    }

    [Test]
    public async Task Cost_WhenCostTrackingDisabled_ReturnsNotEnabledMessage()
    {
        using var h = new SlashCommandHarness(costConfig: null);
        var reply = await h.SendCommandAsync("/cost");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Cost tracking is not enabled");
    }

    [Test]
    public async Task Usage_WhenEnabled_ReturnsDailyMonthlySession()
    {
        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 });
        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Usage (today):");
        reply.ShouldContain("Usage (this month):");
        reply.ShouldContain("Usage (this session):");
    }

    [Test]
    public async Task Usage_WhenEnabled_IncludesDailyLimit()
    {
        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 5.00m, MonthlyLimitUsd = 0 });
        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Daily limit:");
        reply.ShouldContain("$5.00");
    }

    [Test]
    public async Task Usage_WhenEnabled_IncludesMonthlyLimit()
    {
        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 50.00m });
        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Monthly limit:");
        reply.ShouldContain("$50.00");
    }

    [Test]
    public async Task Usage_WhenNoLimitsConfigured_OmitsLimitLines()
    {
        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 });
        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldNotContain("Daily limit:");
        reply.ShouldNotContain("Monthly limit:");
    }

    [Test]
    public async Task Usage_WithNonOpenRouterProvider_OmitsCreditsSection()
    {
        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 });
        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldNotContain("OpenRouter");
    }

    [Test]
    public async Task Usage_WithOpenRouterProvider_ShowsCreditsSection()
    {
        const string keyInfoJson = """
            {"data":{"label":"test-key","limit":10.00,"limit_remaining":8.50,"usage":1.50,"usage_daily":0.25,"usage_monthly":1.50,"is_free_tier":false}}
            """;
        var handler = new ConfigurableHttpHandler(HttpStatusCode.OK, keyInfoJson);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var orProvider = new OpenRouterProvider(factory, "sk-test-key");

        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 },
            providerOverride: orProvider);

        var reply = await h.SendCommandAsync("/usage");
        reply.ShouldNotBeNull();
        reply.ShouldContain("── OpenRouter ──");
        reply.ShouldContain("Credits remaining:");
        reply.ShouldContain("$8.50");
        reply.ShouldContain("test-key");
    }

    [Test]
    public async Task Usage_DoesNotCallProvider()
    {
        using var h = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 });
        await h.SendCommandAsync("/usage");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }

    [Test]
    public async Task Usage_CostAlias_BehavesIdentically()
    {
        using var h1 = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 });
        using var h2 = new SlashCommandHarness(
            costConfig: new CostConfig { Enabled = true, DailyLimitUsd = 0, MonthlyLimitUsd = 0 });
        var usageReply = await h1.SendCommandAsync("/usage");
        var costReply = await h2.SendCommandAsync("/cost");

        usageReply.ShouldNotBeNull();
        costReply.ShouldNotBeNull();
        // Both aliases should produce identical output
        usageReply.ShouldBe(costReply);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /compact — Context compaction
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashCompactHandlerTests
{
    [Test]
    public async Task Compact_WhenContextWindowNotEnabled_ReturnsDisabledMessage()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/compact");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Context compaction is not enabled");
    }

    [Test]
    public async Task Compact_WhenContextWindowNull_ReturnsDisabledMessage()
    {
        using var h = new SlashCommandHarness(contextWindow: null);
        var reply = await h.SendCommandAsync("/compact");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Context compaction is not enabled");
    }

    [Test]
    public async Task Compact_WhenContextWindowExplicitlyDisabled_ReturnsDisabledMessage()
    {
        using var h = new SlashCommandHarness(
            contextWindow: new ContextWindowConfig { Enabled = false });
        var reply = await h.SendCommandAsync("/compact");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Context compaction is not enabled");
    }

    [Test]
    public async Task Compact_WhenEnabled_WithFewMessages_ReturnsNoOpResult()
    {
        using var h = new SlashCommandHarness(
            contextWindow: new ContextWindowConfig { Enabled = true });
        var senderId = "compact-" + Guid.NewGuid().ToString("N")[..8];

        // Pre-populate session with some messages
        var session = new Session { Id = $"telegram:{senderId}" };
        session.Messages.Add(new ChatMessage(MessageRole.User, "Hello"));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, "Hi there"));
        await h.Sessions.SaveAsync(session);

        // With only 2 messages (fewer than keepRecent default of 20),
        // CompactAsync returns them unchanged — this is a no-op.
        var (reply, after) = await h.SendCommandWithSessionAsync("/compact", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Compacted:");
        // Message count should be unchanged since nothing was summarized
        after.Messages.Count.ShouldBe(2);
    }

    [Test]
    public async Task Compact_WhenEnabled_WithManyMessages_ActuallyCompacts()
    {
        using var h = new SlashCommandHarness(
            contextWindow: new ContextWindowConfig { Enabled = true });
        var senderId = "compact-real-" + Guid.NewGuid().ToString("N")[..8];

        // Pre-populate session with 25+ messages (> keepRecent default of 20 + 1 system)
        var session = new Session { Id = $"telegram:{senderId}" };
        for (var i = 0; i < 25; i++)
        {
            session.Messages.Add(new ChatMessage(MessageRole.User, $"User message {i}"));
            session.Messages.Add(new ChatMessage(MessageRole.Assistant, $"Assistant reply {i}"));
        }

        var originalCount = session.Messages.Count; // 50
        await h.Sessions.SaveAsync(session);

        // Queue a ChatAsync response for the compaction summarization call.
        // CompactionService calls provider.ChatAsync for summarization.
        h.Provider.EnqueueChatResponse(new ChatResponse(
            "Summary of earlier conversation about various topics.",
            null, FinishReason.Stop));

        var (reply, after) = await h.SendCommandWithSessionAsync("/compact", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Compacted:");
        reply.ShouldContain($"{originalCount}");
        // After compaction, message count should be less than original
        after.Messages.Count.ShouldBeLessThan(originalCount);
        // Should have kept ~20 recent + 1 summary = ~21 messages
        after.Messages.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Compact_DoesNotCallProviderForRouting()
    {
        using var h = new SlashCommandHarness(
            contextWindow: new ContextWindowConfig { Enabled = true });
        var senderId = "compact-no-llm-" + Guid.NewGuid().ToString("N")[..8];

        var session = new Session { Id = $"telegram:{senderId}" };
        session.Messages.Add(new ChatMessage(MessageRole.User, "Short chat"));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, "Sure"));
        await h.Sessions.SaveAsync(session);

        await h.SendCommandWithSessionAsync("/compact", senderId);
        // The provider should not receive a ChatAsync or StreamAsync for the slash command itself
        // (compaction may call the provider for summarization if there are enough messages,
        // but not for the routing).
        // With only 2 messages, compaction returns early without an LLM call.
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /think on/off/toggle — Thinking mode control
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashThinkHandlerTests
{
    [Test]
    public async Task ThinkOn_SetsShowThinkingTrue()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-on-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync("/think on", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode on");
        session.ShowThinking.ShouldBeTrue();
    }

    [Test]
    public async Task ThinkOff_SetsShowThinkingFalse()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-off-" + Guid.NewGuid().ToString("N")[..8];

        // First enable thinking
        var preSession = new Session { Id = $"telegram:{senderId}" };
        preSession.ShowThinking = true;
        await h.Sessions.SaveAsync(preSession);

        var (reply, session) = await h.SendCommandWithSessionAsync("/think off", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode off");
        session.ShowThinking.ShouldBeFalse();
    }

    [Test]
    public async Task ThinkToggle_TogglesFromOffToOn()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-toggle-on-" + Guid.NewGuid().ToString("N")[..8];

        // ShowThinking defaults to false
        var (reply, session) = await h.SendCommandWithSessionAsync("/think", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode on");
        session.ShowThinking.ShouldBeTrue();
    }

    [Test]
    public async Task ThinkToggle_TogglesFromOnToOff()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-toggle-off-" + Guid.NewGuid().ToString("N")[..8];

        // Pre-set ShowThinking = true
        var preSession = new Session { Id = $"telegram:{senderId}" };
        preSession.ShowThinking = true;
        await h.Sessions.SaveAsync(preSession);

        var (reply, session) = await h.SendCommandWithSessionAsync("/think", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode off");
        session.ShowThinking.ShouldBeFalse();
    }

    [Test]
    public async Task ThinkOn_ReturnReasoningBlockMessage()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-msg-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, _) = await h.SendCommandWithSessionAsync("/think on", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Reasoning blocks will be shown");
    }

    [Test]
    public async Task ThinkOff_ReturnsTerseMessage()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-off-msg-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, _) = await h.SendCommandWithSessionAsync("/think off", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldBe("Thinking mode off.");
    }

    [Test]
    public async Task VerboseOn_WorksSameAsThinkOn()
    {
        using var h = new SlashCommandHarness();
        var senderId = "verbose-on-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync("/verbose on", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode on");
        session.ShowThinking.ShouldBeTrue();
    }

    [Test]
    public async Task VerboseOff_WorksSameAsThinkOff()
    {
        using var h = new SlashCommandHarness();
        var senderId = "verbose-off-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync("/verbose off", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode off");
        session.ShowThinking.ShouldBeFalse();
    }

    /// <summary>
    /// Design decision: only "on" and "off" are recognized arguments.
    /// All other arguments — including common synonyms like "yes", "true",
    /// "enable", and "1" — fall through to toggle, not "turn on".
    /// </summary>
    [TestCase("whatever")]
    [TestCase("yes")]
    [TestCase("true")]
    [TestCase("enable")]
    [TestCase("1")]
    public async Task Think_UnrecognizedArg_TriggersToggle(string arg)
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-unrec-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync($"/think {arg}", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking mode on"); // toggled from default (off) to on
        session.ShowThinking.ShouldBeTrue();
    }

    [Test]
    public async Task Think_PersistsSessionState()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-persist-" + Guid.NewGuid().ToString("N")[..8];

        await h.SendCommandWithSessionAsync("/think on", senderId);

        // Load session independently to verify persistence
        var session = await h.Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        session.ShowThinking.ShouldBeTrue();
    }

    [Test]
    public async Task Think_DoesNotCallProvider()
    {
        using var h = new SlashCommandHarness();
        await h.SendCommandAsync("/think on");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /model — Model switching
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashModelHandlerTests
{
    [Test]
    public async Task Model_NoArgs_ShowsCurrentModel()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "gpt-4o-mini" });
        var reply = await h.SendCommandAsync("/model");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Current model: gpt-4o-mini");
        reply.ShouldContain("config default");
    }

    [Test]
    public async Task Model_NoArgs_ShowsSessionOverride_WhenSet()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "gpt-4o-mini" });
        var senderId = "model-override-" + Guid.NewGuid().ToString("N")[..8];

        // Pre-set model override
        var session = new Session { Id = $"telegram:{senderId}" };
        session.ModelOverride = "claude-opus-4-0520";
        await h.Sessions.SaveAsync(session);

        var (reply, _) = await h.SendCommandWithSessionAsync("/model", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Current model: claude-opus-4-0520");
        reply.ShouldContain("session override");
    }

    [Test]
    public async Task Model_WithName_SetsSessionOverride()
    {
        using var h = new SlashCommandHarness();
        var senderId = "model-set-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync("/model gpt-4o", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model set to: gpt-4o");
        reply.ShouldContain("for this session");
        session.ModelOverride.ShouldBe("gpt-4o");
    }

    [Test]
    public async Task Model_WithComplexName_PreservesFullName()
    {
        using var h = new SlashCommandHarness();
        var senderId = "model-complex-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync(
            "/model anthropic/claude-sonnet-4.5-20250514:beta", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("anthropic/claude-sonnet-4.5-20250514:beta");
        session.ModelOverride.ShouldBe("anthropic/claude-sonnet-4.5-20250514:beta");
    }

    [Test]
    public async Task Model_Reset_ClearsOverride()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "default-model" });
        var senderId = "model-reset-" + Guid.NewGuid().ToString("N")[..8];

        // First set an override
        await h.SendCommandWithSessionAsync("/model gpt-4o", senderId);

        // Then reset
        var (reply, session) = await h.SendCommandWithSessionAsync("/model reset", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model reset to config default: default-model");
        session.ModelOverride.ShouldBeNull();
    }

    [Test]
    public async Task Model_ResetCaseInsensitive_Works()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "default-model" });
        var senderId = "model-reset-ci-" + Guid.NewGuid().ToString("N")[..8];

        await h.SendCommandWithSessionAsync("/model gpt-4o", senderId);
        var (reply, session) = await h.SendCommandWithSessionAsync("/model RESET", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model reset to config default");
        session.ModelOverride.ShouldBeNull();
    }

    [Test]
    public async Task Model_Reset_WhenNoOverride_StillReturnsResetMessage()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "default-model" });
        var senderId = "model-reset-noop-" + Guid.NewGuid().ToString("N")[..8];

        var (reply, session) = await h.SendCommandWithSessionAsync("/model reset", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Model reset to config default: default-model");
        session.ModelOverride.ShouldBeNull();
    }

    [Test]
    public async Task Model_Set_PersistsToSession()
    {
        using var h = new SlashCommandHarness();
        var senderId = "model-persist-" + Guid.NewGuid().ToString("N")[..8];

        await h.SendCommandWithSessionAsync("/model my-custom-model", senderId);

        // Verify persistence by loading session independently
        var session = await h.Sessions.LoadOrCreateAsync($"telegram:{senderId}");
        session.ModelOverride.ShouldBe("my-custom-model");
    }

    [Test]
    public async Task Model_DoesNotCallProvider()
    {
        using var h = new SlashCommandHarness();
        await h.SendCommandAsync("/model gpt-4o");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }

    [Test]
    public async Task Model_WithWhitespace_TrimsArgument()
    {
        using var h = new SlashCommandHarness();
        var senderId = "model-ws-" + Guid.NewGuid().ToString("N")[..8];

        // The router splits on first space with TrimEntries, so "  gpt-4o  "
        // becomes "gpt-4o" after the split. HandleModelCommandAsync also calls Trim().
        // This test verifies whitespace is properly stripped from the stored override.
        var (reply, session) = await h.SendCommandWithSessionAsync("/model   gpt-4o  ", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("gpt-4o");
        session.ModelOverride.ShouldBe("gpt-4o");
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /goals — Goal management
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashGoalsHandlerTests
{
    [Test]
    public async Task Goals_WithNoGoals_ReturnsNoActiveGoals()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/goals");
        reply.ShouldNotBeNull();
        reply.ShouldBe("No active goals.");
    }

    [Test]
    public async Task Goals_WithActiveGoals_ListsThemWithStepCounts()
    {
        using var h = new SlashCommandHarness();

        // Pre-populate goals
        var goals = new List<Goal>
        {
            new()
            {
                Id = "abc12345",
                Title = "Write documentation",
                Status = GoalStatus.Active,
                Steps =
                [
                    new GoalStep { Text = "Draft outline", Done = true },
                    new GoalStep { Text = "Write content", Done = false },
                    new GoalStep { Text = "Review", Done = false }
                ]
            }
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Goals:");
        reply.ShouldContain("abc12345");
        reply.ShouldContain("Write documentation");
        reply.ShouldContain("[1/3 steps]");
        reply.ShouldContain("Active");
    }

    [Test]
    public async Task Goals_WithPausedGoals_IncludesThemInListing()
    {
        using var h = new SlashCommandHarness();

        var goals = new List<Goal>
        {
            new()
            {
                Id = "paused01",
                Title = "Paused goal",
                Status = GoalStatus.Paused,
                Steps = []
            }
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals");
        reply.ShouldNotBeNull();
        reply.ShouldContain("paused01");
        reply.ShouldContain("Paused goal");
        reply.ShouldContain("Paused");
    }

    [Test]
    public async Task Goals_ExcludesCompletedAndDeletedGoals()
    {
        using var h = new SlashCommandHarness();

        var goals = new List<Goal>
        {
            new() { Id = "active01", Title = "Active goal", Status = GoalStatus.Active },
            new() { Id = "done0001", Title = "Completed goal", Status = GoalStatus.Completed },
            new() { Id = "del00001", Title = "Deleted goal", Status = GoalStatus.Deleted },
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals");
        reply.ShouldNotBeNull();
        reply.ShouldContain("active01");
        reply.ShouldNotContain("done0001");
        reply.ShouldNotContain("del00001");
    }

    [Test]
    public async Task Goals_WithNoSteps_OmitsStepCount()
    {
        using var h = new SlashCommandHarness();

        var goals = new List<Goal>
        {
            new() { Id = "nostep01", Title = "Simple task", Status = GoalStatus.Active, Steps = [] }
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals");
        reply.ShouldNotBeNull();
        reply.ShouldContain("nostep01");
        reply.ShouldContain("Simple task");
        // When there are no steps, the step count bracket "[x/y steps]" should not appear
        reply.ShouldNotContain("[0/0");
        reply.ShouldNotContain("steps]");
    }

    [Test]
    public async Task Goals_MultipleGoals_ListsAll()
    {
        using var h = new SlashCommandHarness();

        var goals = new List<Goal>
        {
            new() { Id = "goal0001", Title = "First goal", Status = GoalStatus.Active },
            new() { Id = "goal0002", Title = "Second goal", Status = GoalStatus.Active },
            new() { Id = "goal0003", Title = "Third goal", Status = GoalStatus.Paused },
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals");
        reply.ShouldNotBeNull();
        reply.ShouldContain("goal0001");
        reply.ShouldContain("goal0002");
        reply.ShouldContain("goal0003");
        reply.ShouldContain("First goal");
        reply.ShouldContain("Second goal");
        reply.ShouldContain("Third goal");
    }

    [Test]
    public async Task GoalsClear_WithActiveGoals_ClearsThemAndReturnsCount()
    {
        using var h = new SlashCommandHarness();

        var goals = new List<Goal>
        {
            new() { Id = "clr00001", Title = "Goal A", Status = GoalStatus.Active },
            new() { Id = "clr00002", Title = "Goal B", Status = GoalStatus.Paused },
            new() { Id = "clr00003", Title = "Goal C", Status = GoalStatus.Completed },
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals clear");
        reply.ShouldNotBeNull();
        reply.ShouldContain("Cleared 2 goal(s).");

        // Verify goals were actually marked as Deleted
        var updated = await h.GoalStorage.LoadAsync();
        updated.Where(g => g.Status == GoalStatus.Deleted).Count().ShouldBe(2);
        updated.Single(g => g.Status == GoalStatus.Completed).Id.ShouldBe("clr00003");
    }

    [Test]
    public async Task GoalsClear_WithNoActiveGoals_ReturnsNoneToCleanMessage()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/goals clear");
        reply.ShouldNotBeNull();
        reply.ShouldBe("No active or paused goals to clear.");
    }

    [Test]
    public async Task GoalsClear_WithOnlyCompletedGoals_ReturnsNoneToCleanMessage()
    {
        using var h = new SlashCommandHarness();

        var goals = new List<Goal>
        {
            new() { Id = "done0001", Title = "Done goal", Status = GoalStatus.Completed },
        };
        await h.GoalStorage.SaveAsync(goals);

        var reply = await h.SendCommandAsync("/goals clear");
        reply.ShouldNotBeNull();
        reply.ShouldBe("No active or paused goals to clear.");
    }

    [Test]
    public async Task GoalsClear_WhenSaveFails_ReturnsErrorMessage()
    {
        using var h = new SlashCommandHarness();

        // Save a valid goal first
        var goals = new List<Goal>
        {
            new() { Id = "err00001", Title = "Will fail on clear", Status = GoalStatus.Active },
        };
        await h.GoalStorage.SaveAsync(goals);

        // Replace the goals.json file with a directory of the same name.
        // This causes the atomic File.Move in SaveAsync to throw, exercising
        // the handler's catch block which returns "Error: failed to clear goals."
        var goalsPath = h.GoalStorage.GoalsPathOverride!;
        File.Delete(goalsPath);
        Directory.CreateDirectory(goalsPath);

        var reply = await h.SendCommandAsync("/goals clear");
        reply.ShouldNotBeNull();
        reply.ShouldBe("Error: failed to clear goals.");

        // Clean up the directory so harness disposal works
        Directory.Delete(goalsPath, recursive: true);
    }

    [Test]
    public async Task Goals_DoesNotCallProvider()
    {
        using var h = new SlashCommandHarness();
        await h.SendCommandAsync("/goals");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  /models — Model listing (OpenRouter)
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashModelsHandlerTests
{
    [Test]
    public async Task Models_WithNonOpenRouterProvider_ReturnsOnlyAvailableMessage()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/models");
        reply.ShouldNotBeNull();
        reply.ShouldContain("only available for the OpenRouter provider");
    }

    [Test]
    public async Task Models_DoesNotCallProviderForNonOpenRouter()
    {
        using var h = new SlashCommandHarness();
        await h.SendCommandAsync("/models");
        h.Provider.ReceivedRequests.Count.ShouldBe(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Cross-cutting concerns
// ════════════════════════════════════════════════════════════════════════

[TestFixture]
public sealed class SlashCommandCrossCuttingTests
{
    [Test]
    public async Task UnknownSlashCommand_IsNotIntercepted()
    {
        // Unknown slash commands should pass through to the LLM, not be handled as slash commands.
        // The router returns null for unknown commands, so ProcessMessageAsync goes to the LLM path.
        using var h = new SlashCommandHarness();
        h.Provider.EnqueueTextStream("LLM response to unknown command");

        await h.SendCommandAsync("/randomcommand");

        // Provider should have been called (not intercepted as slash command)
        h.Provider.ReceivedRequests.Count.ShouldBe(1);
    }

    [Test]
    public async Task ArgumentTooLong_ReturnsError()
    {
        using var h = new SlashCommandHarness();
        var longArg = new string('x', SlashCommandRouter.MaxArgumentLength + 1);
        var reply = await h.SendCommandAsync($"/model {longArg}");
        reply.ShouldNotBeNull();
        reply.ShouldContain("too long");
    }

    [Test]
    public void EmptyText_IsNotIntercepted()
    {
        SlashCommandRouter.TryHandle("").ShouldBeNull();
    }

    [Test]
    public void NonSlashText_IsNotIntercepted()
    {
        SlashCommandRouter.TryHandle("Hello, how are you?").ShouldBeNull();
    }

    [Test]
    public async Task SlashCommand_CaseInsensitive()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("/CLEAR");
        reply.ShouldBe("Session cleared.");
    }

    [Test]
    public async Task SlashCommand_WithLeadingWhitespace()
    {
        using var h = new SlashCommandHarness();
        var reply = await h.SendCommandAsync("  /clear  ");
        reply.ShouldBe("Session cleared.");
    }

    [Test]
    public async Task MultipleSlashCommands_SameSession_WorkSequentially()
    {
        using var h = new SlashCommandHarness(new AgentDefaults { Model = "test-model" });
        var senderId = "sequential-" + Guid.NewGuid().ToString("N")[..8];

        // /model set
        var (reply1, session1) = await h.SendCommandWithSessionAsync("/model gpt-4o", senderId);
        reply1.ShouldNotBeNull();
        reply1.ShouldContain("Model set to: gpt-4o");
        session1.ModelOverride.ShouldBe("gpt-4o");

        // /status should show the override
        var (reply2, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply2.ShouldNotBeNull();
        reply2.ShouldContain("Model: gpt-4o");
        reply2.ShouldContain("(session override)");

        // /model reset
        var (reply3, session3) = await h.SendCommandWithSessionAsync("/model reset", senderId);
        reply3.ShouldNotBeNull();
        reply3.ShouldContain("Model reset to config default: test-model");
        session3.ModelOverride.ShouldBeNull();

        // /status should show default again
        var (reply4, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply4.ShouldNotBeNull();
        reply4.ShouldContain("Model: test-model");
        reply4.ShouldNotContain("session override");
    }

    [Test]
    public async Task ThinkThenStatus_ReflectsThinkingState()
    {
        using var h = new SlashCommandHarness();
        var senderId = "think-status-" + Guid.NewGuid().ToString("N")[..8];

        // Enable thinking
        await h.SendCommandWithSessionAsync("/think on", senderId);

        // Status should show thinking: on
        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Thinking: on");
    }

    [Test]
    public async Task ClearThenStatus_ShowsZeroMessages()
    {
        using var h = new SlashCommandHarness();
        var senderId = "clear-status-" + Guid.NewGuid().ToString("N")[..8];

        // Pre-populate session
        var session = new Session { Id = $"telegram:{senderId}" };
        session.Messages.Add(new ChatMessage(MessageRole.User, "Hi"));
        session.Messages.Add(new ChatMessage(MessageRole.Assistant, "Hello"));
        await h.Sessions.SaveAsync(session);

        // Clear
        await h.SendCommandWithSessionAsync("/clear", senderId);

        // Status should show 0 messages
        var (reply, _) = await h.SendCommandWithSessionAsync("/status", senderId);
        reply.ShouldNotBeNull();
        reply.ShouldContain("Messages: 0");
    }
}
