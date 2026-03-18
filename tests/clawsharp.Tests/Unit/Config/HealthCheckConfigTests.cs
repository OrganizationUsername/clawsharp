using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Config;

/// <summary>
/// Unit tests for <see cref="HealthCheckConfig"/> defaults and integration with <see cref="AgentDefaults"/>.
/// </summary>
[TestFixture]
public sealed class HealthCheckConfigTests
{
    // -- Default values --

    [Test]
    public void HealthCheckConfig_DefaultEnabled_IsTrue()
    {
        var cfg = new HealthCheckConfig();
        cfg.Enabled.ShouldBeTrue();
    }

    [Test]
    public void HealthCheckConfig_DefaultInterval_Is5Minutes()
    {
        var cfg = new HealthCheckConfig();
        cfg.Interval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void HealthCheckConfig_DefaultCheckOnStartup_IsTrue()
    {
        var cfg = new HealthCheckConfig();
        cfg.CheckOnStartup.ShouldBeTrue();
    }

    // -- Custom values --

    [Test]
    public void HealthCheckConfig_CustomValues_Preserved()
    {
        var cfg = new HealthCheckConfig
        {
            Enabled = false,
            Interval = TimeSpan.FromSeconds(60),
            CheckOnStartup = false
        };

        cfg.Enabled.ShouldBeFalse();
        cfg.Interval.ShouldBe(TimeSpan.FromSeconds(60));
        cfg.CheckOnStartup.ShouldBeFalse();
    }

    // -- AgentDefaults integration --

    [Test]
    public void AgentDefaults_HealthCheckNull_ByDefault()
    {
        var defaults = new AgentDefaults();
        defaults.HealthCheck.ShouldBeNull();
    }

    [Test]
    public void AgentDefaults_HealthCheckSet_Preserved()
    {
        var defaults = new AgentDefaults
        {
            HealthCheck = new HealthCheckConfig
            {
                Enabled = true,
                Interval = TimeSpan.FromSeconds(120),
                CheckOnStartup = false
            }
        };

        defaults.HealthCheck.ShouldNotBeNull();
        defaults.HealthCheck!.Enabled.ShouldBeTrue();
        defaults.HealthCheck.Interval.ShouldBe(TimeSpan.FromSeconds(120));
        defaults.HealthCheck.CheckOnStartup.ShouldBeFalse();
    }

    [Test]
    public void AgentDefaults_HealthCheckDisabled_EnabledFalse()
    {
        var defaults = new AgentDefaults
        {
            HealthCheck = new HealthCheckConfig { Enabled = false }
        };

        defaults.HealthCheck!.Enabled.ShouldBeFalse();
        // Other defaults still apply
        defaults.HealthCheck.Interval.ShouldBe(TimeSpan.FromMinutes(5));
        defaults.HealthCheck.CheckOnStartup.ShouldBeTrue();
    }
}
