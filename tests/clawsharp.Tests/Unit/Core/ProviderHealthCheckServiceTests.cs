using System.Net;
using Clawsharp.Config;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;
using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Unit.Core;

/// <summary>
/// Unit tests for <see cref="ProviderHealthCheckService"/>.
/// Verifies startup checks, periodic checks, skipping non-checkable providers,
/// and cancellation behavior.
/// </summary>
[TestFixture]
public sealed class ProviderHealthCheckServiceTests
{
    // -- 1. Constructor throws when HealthCheck config is null --

    [Test]
    public void Constructor_NullHealthCheckConfig_ThrowsInvalidOperation()
    {
        var provider = new FakeHealthCheckableProvider(healthy: true);
        var config = CreateConfig(useHealthCheck: false);

        Should.Throw<InvalidOperationException>(() =>
            new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance));
    }

    // -- 2. Startup check logs healthy when provider is healthy --

    [Test]
    public async Task ExecuteAsync_HealthyProviderOnStartup_CompletesWithoutError()
    {
        var provider = new FakeHealthCheckableProvider(healthy: true);
        var config = CreateConfig(checkOnStartup: true, intervalSeconds: 3600);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        // Provider was checked at least once on startup
        provider.CheckCount.ShouldBeGreaterThan(0);
    }

    // -- 3. Startup check with unhealthy provider --

    [Test]
    public async Task ExecuteAsync_UnhealthyProviderOnStartup_ProviderChecked()
    {
        var provider = new FakeHealthCheckableProvider(healthy: false, message: "HTTP 503 Service Unavailable");
        var config = CreateConfig(checkOnStartup: true, intervalSeconds: 3600);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        provider.CheckCount.ShouldBeGreaterThan(0);
    }

    // -- 4. Provider that doesn't implement IHealthCheckableProvider is skipped --

    [Test]
    public async Task ExecuteAsync_NonCheckableProvider_Skipped()
    {
        var provider = new FakeNonCheckableProvider();
        var config = CreateConfig(checkOnStartup: true, intervalSeconds: 3600);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        // No error should occur -- service gracefully skips non-checkable providers
        provider.ChatCallCount.ShouldBe(0);
    }

    // -- 5. CheckOnStartup disabled skips startup check --

    [Test]
    public async Task ExecuteAsync_CheckOnStartupFalse_SkipsStartupCheck()
    {
        var provider = new FakeHealthCheckableProvider(healthy: true);
        // Long interval so periodic check doesn't fire within test window
        var config = CreateConfig(checkOnStartup: false, intervalSeconds: 3600);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        // Should not have been checked within 500ms (interval is 3600s)
        provider.CheckCount.ShouldBe(0);
    }

    // -- 6. Service respects cancellation --

    [Test]
    public async Task ExecuteAsync_CancellationRequested_StopsGracefully()
    {
        var provider = new FakeHealthCheckableProvider(healthy: true);
        var config = CreateConfig(checkOnStartup: false, intervalSeconds: 1);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Cancel immediately
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Should not throw -- graceful shutdown
    }

    // -- 7. Provider that throws during health check doesn't crash service --

    [Test]
    public async Task ExecuteAsync_ProviderThrowsOnCheck_ServiceContinues()
    {
        var provider = new FakeHealthCheckableProvider(throwOnCheck: true);
        var config = CreateConfig(checkOnStartup: true, intervalSeconds: 3600);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        // Provider was called even though it threw
        provider.CheckCount.ShouldBeGreaterThan(0);
    }

    // -- 8. Periodic check fires after interval --

    [Test]
    public async Task ExecuteAsync_PeriodicCheck_FiresAfterInterval()
    {
        var provider = new FakeHealthCheckableProvider(healthy: true);
        // Short interval (1 second) with startup check off
        var config = CreateConfig(checkOnStartup: false, intervalSeconds: 1);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        // Should have fired at least once after the 1-second interval within 3 seconds
        provider.CheckCount.ShouldBeGreaterThan(0);
    }

    // -- 9. Startup + periodic both fire --

    [Test]
    public async Task ExecuteAsync_StartupAndPeriodic_BothFire()
    {
        var provider = new FakeHealthCheckableProvider(healthy: true);
        var config = CreateConfig(checkOnStartup: true, intervalSeconds: 1);

        using var service = new ProviderHealthCheckService(provider, config, new StubHttpClientFactory(), NullLogger<ProviderHealthCheckService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await service.StartAsync(cts.Token);
        await WaitForCancellationAsync(cts);
        await service.StopAsync(CancellationToken.None);

        // At least startup (1) + one periodic (1) = 2+
        provider.CheckCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    // -- Helpers --

    private static IOptions<AppConfig> CreateConfig(
        bool checkOnStartup = true,
        int intervalSeconds = 300,
        HealthCheckConfig? healthCheck = null,
        bool useHealthCheck = true)
    {
        var appConfig = new AppConfig
        {
            Agents = new AgentConfig
            {
                Defaults = new AgentDefaults()
            }
        };

        if (useHealthCheck)
        {
            appConfig.Agents.Defaults.HealthCheck = healthCheck ?? new HealthCheckConfig
            {
                Enabled = true,
                CheckOnStartup = checkOnStartup,
                IntervalSeconds = intervalSeconds
            };
        }

        return Options.Create(appConfig);
    }

    private static async Task WaitForCancellationAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }
}

// ──────────────────────────────────────────────────────────────────────
//  Fakes for ProviderHealthCheckService tests
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Fake provider that implements both IProvider and IHealthCheckableProvider.
/// </summary>
internal sealed class FakeHealthCheckableProvider : IProvider, IHealthCheckableProvider
{
    private readonly bool _healthy;

    private readonly string? _message;

    private readonly bool _throwOnCheck;

    private int _checkCount;

    public FakeHealthCheckableProvider(bool healthy = true, string? message = null, bool throwOnCheck = false)
    {
        _healthy = healthy;
        _message = message;
        _throwOnCheck = throwOnCheck;
    }

    public string Name => "fake-healthcheckable";

    public bool SupportsVision => false;

    public int CheckCount => _checkCount;

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        throw new NotSupportedException("ChatAsync not used in health check tests.");
    }

    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _checkCount);

        if (_throwOnCheck)
        {
            throw new HttpRequestException("Simulated connection failure");
        }

        var result = new HealthCheckResult(_healthy, _message ?? (_healthy ? "HTTP 200" : "HTTP 503"), TimeSpan.FromMilliseconds(10));
        return Task.FromResult(result);
    }
}

/// <summary>
/// Fake provider that does NOT implement IHealthCheckableProvider.
/// Used to verify that non-checkable providers are gracefully skipped.
/// </summary>
internal sealed class FakeNonCheckableProvider : IProvider
{
    private int _chatCallCount;

    public string Name => "fake-noncheckable";

    public bool SupportsVision => false;

    public int ChatCallCount => _chatCallCount;

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _chatCallCount);
        throw new NotSupportedException("ChatAsync not used in health check tests.");
    }
}

/// <summary>
/// Minimal IHttpClientFactory stub that returns a basic HttpClient.
/// Not used for actual HTTP calls in unit tests; just satisfies the constructor dependency.
/// </summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
