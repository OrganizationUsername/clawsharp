using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Core;

/// <summary>
/// Unit tests for <see cref="HeartbeatService"/>.
/// Verifies schedule-based firing, disabled skip, and InboundMessage.IsHeartbeat flag.
/// </summary>
[TestFixture]
public sealed class HeartbeatServiceTests
{
    // ── 1. Heartbeat disabled: service should stop without publishing ──

    [Test]
    public async Task ExecuteAsync_DisabledConfig_DoesNotPublish()
    {
        var bus = new CapturingMessageBus();
        var config = CreateConfig(enabled: false);

        var service = new HeartbeatService(bus, config, NullLogger<HeartbeatService>.Instance);

        // HeartbeatService throws InvalidOperationException when Heartbeat config is null,
        // but when Enabled=false it should never be registered (GatewayHost gates on Enabled).
        // We test that if it WERE registered with Enabled=false, the internal null check
        // on HeartbeatConfig fires. The constructor requires Heartbeat to be non-null, so
        // we provide it with Enabled=false and verify the loop exits quickly via cancellation
        // without ever publishing.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // StartAsync + let it run briefly
        await service.StartAsync(cts.Token);
        try
        {
            await Task.Delay(250, CancellationToken.None);
        }
        catch
        {
            /* swallow */
        }

        await service.StopAsync(CancellationToken.None);

        bus.Published.ShouldBeEmpty("HeartbeatService should not publish when the cron schedule does not match within the test window");
    }

    // ── 2. Heartbeat fires when enabled and cron matches ──

    [Test]
    public async Task ExecuteAsync_EnabledWithMatchingSchedule_PublishesHeartbeat()
    {
        var bus = new CapturingMessageBus();
        // Use "* * * * *" (every minute) so the cron always matches
        var config = CreateConfig(enabled: true, schedule: "* * * * *", channel: "telegram");

        var service = new HeartbeatService(bus, config, NullLogger<HeartbeatService>.Instance);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Wait for up to 15 seconds for a heartbeat to be published (10s poll interval + margin)
        var published = await WaitForPublishAsync(bus, timeout: TimeSpan.FromSeconds(15));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        published.ShouldNotBeNull("Expected at least one heartbeat message to be published");
        published!.IsHeartbeat.ShouldBeTrue();
        published.SenderId.ShouldBe("heartbeat");
        published.SenderName.ShouldBe("heartbeat");
        published.Channel.ShouldBe(ChannelName.Telegram);
        published.Text.ShouldNotBeNullOrWhiteSpace();
    }

    // ── 3. InboundMessage.IsHeartbeat defaults to false ──

    [Test]
    public void InboundMessage_IsHeartbeat_DefaultsFalse()
    {
        var msg = new InboundMessage(
            Channel: ChannelName.Cli,
            SenderId: "user1",
            SenderName: "User",
            Text: "hello");

        msg.IsHeartbeat.ShouldBeFalse();
    }

    // ── 4. InboundMessage with IsHeartbeat=true preserves flag ──

    [Test]
    public void InboundMessage_WithIsHeartbeatTrue_PreservesFlag()
    {
        var msg = new InboundMessage(
            Channel: ChannelName.Cli,
            SenderId: "heartbeat",
            SenderName: "heartbeat",
            Text: "Check tasks",
            IsHeartbeat: true);

        msg.IsHeartbeat.ShouldBeTrue();
    }

    // ── 5. Heartbeat falls back to CLI channel for invalid channel name ──

    [Test]
    public async Task Constructor_InvalidChannel_FallsBackToCli()
    {
        var bus = new CapturingMessageBus();
        var config = CreateConfig(enabled: true, schedule: "* * * * *", channel: "nonexistent_channel");

        var service = new HeartbeatService(bus, config, NullLogger<HeartbeatService>.Instance);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var published = await WaitForPublishAsync(bus, timeout: TimeSpan.FromSeconds(15));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        published.ShouldNotBeNull("Expected at least one heartbeat message to be published");
        // Invalid channel name should fall back to CLI
        published!.Channel.ShouldBe(ChannelName.Cli);
    }

    // ── 6. Heartbeat deduplicates within the same minute ──

    [Test]
    public async Task ExecuteAsync_SameMinute_PublishesAtMostOncePerMinute()
    {
        var bus = new CapturingMessageBus();
        var config = CreateConfig(enabled: true, schedule: "* * * * *", channel: "cli");

        var service = new HeartbeatService(bus, config, NullLogger<HeartbeatService>.Instance);

        var startMinute = DateTimeOffset.Now.Minute;
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        // Wait long enough for multiple poll cycles (10s each). 25s gives 2+ polls.
        await Task.Delay(TimeSpan.FromSeconds(25), CancellationToken.None);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        var endMinute = DateTimeOffset.Now.Minute;
        var crossedMinuteBoundary = endMinute != startMinute;

        // The invariant is "at most once per calendar minute."
        // If the test stayed within one minute: exactly 1 publish.
        // If it crossed a minute boundary: at most 2 (one per minute).
        var maxExpected = crossedMinuteBoundary ? 2 : 1;
        bus.Published.Count.ShouldBeGreaterThan(0, "Expected at least one heartbeat");
        bus.Published.Count.ShouldBeLessThanOrEqualTo(maxExpected,
            $"Heartbeat should fire at most once per calendar minute (crossedBoundary={crossedMinuteBoundary})");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static IOptions<AppConfig> CreateConfig(
        bool enabled,
        string schedule = "*/30 * * * *",
        string channel = "cli")
    {
        var appConfig = new AppConfig
        {
            Agents = new AgentConfig
            {
                Defaults = new AgentDefaults
                {
                    Heartbeat = new HeartbeatConfig
                    {
                        Enabled = enabled,
                        Schedule = schedule,
                        Channel = channel,
                    }
                }
            }
        };
        return Options.Create(appConfig);
    }

    private static async Task<InboundMessage?> WaitForPublishAsync(
        CapturingMessageBus bus, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (bus.Published.Count > 0)
                {
                    return bus.Published[0];
                }

                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return bus.Published.Count > 0 ? bus.Published[0] : null;
    }
}

/// <summary>
/// Simple IMessageBus that captures all published messages for test assertions.
/// </summary>
internal sealed class CapturingMessageBus : IMessageBus
{
    private readonly List<InboundMessage> _published = [];

    private readonly object _lock = new();

    public IReadOnlyList<InboundMessage> Published
    {
        get
        {
            lock (_lock)
            {
                return [.. _published];
            }
        }
    }

    public ValueTask PublishAsync(InboundMessage message, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _published.Add(message);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<InboundMessage> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        // Not used by HeartbeatService tests
        await Task.CompletedTask;
        yield break;
    }
}