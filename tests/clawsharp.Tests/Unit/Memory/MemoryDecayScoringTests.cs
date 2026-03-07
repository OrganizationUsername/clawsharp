using Clawsharp.Memory;

namespace Clawsharp.Tests.Unit.Memory;

[TestFixture]
public sealed class MemoryDecayScoringTests
{
    [Test]
    public void ApplyDecay_OneHalfLife_HalvesScore()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-7);
        var result = MemoryDecayScoring.ApplyDecay(1.0f, createdAt, halfLifeDays: 7);
        result.ShouldBe(0.5f, 0.05f);
    }

    [Test]
    public void ApplyDecay_DefaultCreatedAt_ReturnsOriginalScore()
    {
        var result = MemoryDecayScoring.ApplyDecay(0.8f, default, halfLifeDays: 7);
        result.ShouldBe(0.8f);
    }

    [Test]
    public void ApplyDecay_VeryOldFact_ScoreNearZero()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-365);
        var result = MemoryDecayScoring.ApplyDecay(1.0f, createdAt, halfLifeDays: 7);
        result.ShouldBeLessThan(0.001f);
    }

    [Test]
    public void ApplyDecay_RecentFact_BarelyChanges()
    {
        var createdAt = DateTimeOffset.UtcNow.AddHours(-1);
        var result = MemoryDecayScoring.ApplyDecay(1.0f, createdAt, halfLifeDays: 7);
        result.ShouldBeGreaterThan(0.99f);
    }

    [Test]
    public void ApplyDecay_TwoHalfLives_QuarterScore()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-14);
        var result = MemoryDecayScoring.ApplyDecay(1.0f, createdAt, halfLifeDays: 7);
        result.ShouldBe(0.25f, 0.05f);
    }

    [Test]
    public void ApplyDecay_ZeroHalfLife_ReturnsOriginal()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-30);
        var result = MemoryDecayScoring.ApplyDecay(0.9f, createdAt, halfLifeDays: 0);
        result.ShouldBe(0.9f);
    }

    [Test]
    public void ApplyDecay_NegativeHalfLife_ReturnsOriginal()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-30);
        var result = MemoryDecayScoring.ApplyDecay(0.9f, createdAt, halfLifeDays: -1);
        result.ShouldBe(0.9f);
    }

    [Test]
    public void ApplyDecay_LargeHalfLife_SlowDecay()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-30);
        var result = MemoryDecayScoring.ApplyDecay(1.0f, createdAt, halfLifeDays: 365);
        // 30 days with halfLife=365 → 2^(-30/365) ≈ 0.945
        result.ShouldBeGreaterThan(0.9f);
    }
}