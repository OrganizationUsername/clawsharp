using System.ComponentModel.DataAnnotations;
using Clawsharp.Config.Features;

namespace Clawsharp.Tests.Unit.Config;

public sealed class ResilienceConfigTests
{
    private static List<ValidationResult> Validate(object obj)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(obj);
        Validator.TryValidateObject(obj, context, results, validateAllProperties: true);
        return results;
    }

    [Test]
    public void DefaultValues_AreValid()
    {
        var config = new ResilienceConfig();
        Validate(config).ShouldBeEmpty();
    }

    [Test]
    public void RetryDefaults_AreValid()
    {
        var config = new RetryPolicyConfig();
        Validate(config).ShouldBeEmpty();

        config.MaxRetryAttempts.ShouldBe(3);
        config.Delay.ShouldBe(TimeSpan.FromSeconds(1));
        config.MaxDelay.ShouldBe(TimeSpan.FromSeconds(30));
        config.BackoffType.ShouldBe("exponential");
        config.UseJitter.ShouldBeTrue();
    }

    [TestCase(-2)]
    [TestCase(11)]
    public void RetryMaxAttempts_OutOfRange_FailsValidation(int value)
    {
        var config = new RetryPolicyConfig { MaxRetryAttempts = value };
        Validate(config).ShouldNotBeEmpty();
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(5)]
    [TestCase(10)]
    public void RetryMaxAttempts_InRange_PassesValidation(int value)
    {
        var config = new RetryPolicyConfig { MaxRetryAttempts = value };
        var results = Validate(config);
        results.ShouldNotContain(r => r.MemberNames.Contains(nameof(RetryPolicyConfig.MaxRetryAttempts)));
    }

    [Test]
    public void RequestTimeout_Default_Is120Seconds()
    {
        var config = new ResilienceConfig();
        config.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Test]
    public void RequestTimeout_CustomValue_Preserved()
    {
        var config = new ResilienceConfig { RequestTimeout = TimeSpan.FromSeconds(60) };
        config.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void CircuitBreakerDefaults_AreValid()
    {
        var config = new CircuitBreakerConfig();
        Validate(config).ShouldBeEmpty();
    }

    [TestCase(0.0)]
    [TestCase(0.05)]
    public void CircuitBreaker_FailureRatioTooLow_FailsValidation(double value)
    {
        var config = new CircuitBreakerConfig { FailureRatio = value };
        Validate(config).ShouldNotBeEmpty();
    }

    [TestCase(0.1)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public void CircuitBreaker_FailureRatioInRange_PassesValidation(double value)
    {
        var config = new CircuitBreakerConfig { FailureRatio = value };
        var results = Validate(config);
        results.ShouldNotContain(r => r.MemberNames.Contains(nameof(CircuitBreakerConfig.FailureRatio)));
    }

    [TestCase(1)]
    [TestCase(101)]
    public void CircuitBreaker_MinThroughputOutOfRange_FailsValidation(int value)
    {
        var config = new CircuitBreakerConfig { MinimumThroughput = value };
        Validate(config).ShouldNotBeEmpty();
    }

    [Test]
    public void CircuitBreaker_BreakDuration_Default_Is30Seconds()
    {
        var config = new CircuitBreakerConfig();
        config.BreakDuration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void CircuitBreaker_BreakDuration_CustomValue_Preserved()
    {
        var config = new CircuitBreakerConfig { BreakDuration = TimeSpan.FromSeconds(60) };
        config.BreakDuration.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void CircuitBreaker_SamplingDuration_Default_Is30Seconds()
    {
        var config = new CircuitBreakerConfig();
        config.SamplingDuration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void CircuitBreaker_SamplingDuration_CustomValue_Preserved()
    {
        var config = new CircuitBreakerConfig { SamplingDuration = TimeSpan.FromMinutes(2) };
        config.SamplingDuration.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Test]
    public void RetryDelay_Default_Is1Second()
    {
        var config = new RetryPolicyConfig();
        config.Delay.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void RetryDelay_CustomValue_Preserved()
    {
        var config = new RetryPolicyConfig { Delay = TimeSpan.FromMilliseconds(500) };
        config.Delay.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    [Test]
    public void RetryMaxDelay_Default_Is30Seconds()
    {
        var config = new RetryPolicyConfig();
        config.MaxDelay.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void RetryMaxDelay_CustomValue_Preserved()
    {
        var config = new RetryPolicyConfig { MaxDelay = TimeSpan.FromMinutes(5) };
        config.MaxDelay.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
