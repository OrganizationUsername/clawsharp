using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Unit tests for <see cref="HealthCheckResult"/> record.
/// Verifies construction, default values, and equality semantics.
/// </summary>
[TestFixture]
public sealed class HealthCheckResultTests
{
    [Test]
    public void Constructor_HealthyWithAllFields_PropertiesSet()
    {
        var elapsed = TimeSpan.FromMilliseconds(42);
        var result = new HealthCheckResult(true, "HTTP 200", elapsed);

        result.IsHealthy.ShouldBeTrue();
        result.Message.ShouldBe("HTTP 200");
        result.ResponseTime.ShouldBe(elapsed);
    }

    [Test]
    public void Constructor_UnhealthyWithMessage_PropertiesSet()
    {
        var result = new HealthCheckResult(false, "HTTP 503 Service Unavailable", TimeSpan.FromSeconds(1));

        result.IsHealthy.ShouldBeFalse();
        result.Message.ShouldBe("HTTP 503 Service Unavailable");
        result.ResponseTime.ShouldNotBeNull();
    }

    [Test]
    public void Constructor_DefaultMessage_IsNull()
    {
        var result = new HealthCheckResult(true);

        result.Message.ShouldBeNull();
    }

    [Test]
    public void Constructor_DefaultResponseTime_IsNull()
    {
        var result = new HealthCheckResult(true);

        result.ResponseTime.ShouldBeNull();
    }

    [Test]
    public void Constructor_BothDefaultsNull_WhenOnlyIsHealthyProvided()
    {
        var result = new HealthCheckResult(false);

        result.IsHealthy.ShouldBeFalse();
        result.Message.ShouldBeNull();
        result.ResponseTime.ShouldBeNull();
    }

    [Test]
    public void Constructor_MessageProvided_ResponseTimeDefaultsNull()
    {
        var result = new HealthCheckResult(true, "OK");

        result.Message.ShouldBe("OK");
        result.ResponseTime.ShouldBeNull();
    }

    [Test]
    public void Equality_SameValues_AreEqual()
    {
        var elapsed = TimeSpan.FromMilliseconds(50);
        var a = new HealthCheckResult(true, "HTTP 200", elapsed);
        var b = new HealthCheckResult(true, "HTTP 200", elapsed);

        a.ShouldBe(b);
    }

    [Test]
    public void Equality_DifferentIsHealthy_NotEqual()
    {
        var a = new HealthCheckResult(true, "msg");
        var b = new HealthCheckResult(false, "msg");

        a.ShouldNotBe(b);
    }

    [Test]
    public void Equality_DifferentMessage_NotEqual()
    {
        var a = new HealthCheckResult(true, "HTTP 200");
        var b = new HealthCheckResult(true, "HTTP 500");

        a.ShouldNotBe(b);
    }

    [Test]
    public void Equality_DifferentResponseTime_NotEqual()
    {
        var a = new HealthCheckResult(true, "OK", TimeSpan.FromMilliseconds(10));
        var b = new HealthCheckResult(true, "OK", TimeSpan.FromMilliseconds(20));

        a.ShouldNotBe(b);
    }
}
