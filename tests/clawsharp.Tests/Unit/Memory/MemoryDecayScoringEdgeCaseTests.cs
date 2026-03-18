using Clawsharp.Memory;

namespace Clawsharp.Tests.Unit.Memory;

[TestFixture]
public sealed class MemoryDecayScoringEdgeCaseTests
{
    // ── ApplyDecay: future-dated facts ───────────────────────────────

    [Test]
    public void ApplyDecay_FutureDateFact_ReturnsOriginalScore()
    {
        // Clock skew or future-dated fact: ageDays < 0 → early return
        var futureDate = DateTimeOffset.UtcNow.AddDays(1);

        var result = MemoryDecayScoring.ApplyDecay(0.9f, futureDate, halfLifeDays: 7);

        result.ShouldBe(0.9f, "future-dated facts must return the original score unchanged");
    }

    [Test]
    public void ApplyDecay_ExactlyNow_ReturnsOriginalScore()
    {
        // ageDays ≈ 0 → early return (ageDays <= 0)
        var now = DateTimeOffset.UtcNow;

        var result = MemoryDecayScoring.ApplyDecay(1.0f, now, halfLifeDays: 7);

        result.ShouldBeGreaterThanOrEqualTo(0.99f,
            "a fact created right now should barely decay");
    }

    [Test]
    public void ApplyDecay_DefaultCreatedAt_ReturnsOriginalScore()
    {
        // default(DateTimeOffset) == 0001-01-01: handled by createdAt == default check
        var result = MemoryDecayScoring.ApplyDecay(0.7f, default, halfLifeDays: 7);

        result.ShouldBe(0.7f);
    }

    // ── ApplyDecayWithUsage: null lastAccessedAt ──────────────────────

    [Test]
    public void ApplyDecayWithUsage_NullLastAccessedAt_UsesCreatedAtForRecency()
    {
        // With accessCount > 0 and lastAccessedAt = null, daysSinceAccess uses createdAt
        var recentCreate = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = MemoryDecayScoring.ApplyDecayWithUsage(
            score: 0.5f,
            createdAt: recentCreate,
            halfLifeDays: 7,
            accessCount: 3,
            lastAccessedAt: null);

        // Recent creation + access count = boost applied
        result.ShouldBeGreaterThanOrEqualTo(0.5f,
            "usage boost must not reduce the score below base decayed value");
    }

    [Test]
    public void ApplyDecayWithUsage_ZeroAccessCount_NoBoost()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-3);

        var withBoost = MemoryDecayScoring.ApplyDecayWithUsage(
            0.8f, createdAt, 7, accessCount: 5);
        var withoutBoost = MemoryDecayScoring.ApplyDecay(0.8f, createdAt, 7);

        withBoost.ShouldBeGreaterThan(withoutBoost,
            "positive access count must produce a higher score than no access");
    }

    [Test]
    public void ApplyDecayWithUsage_ScoreCappedAtOne()
    {
        // Very high access count + recent create = boost might push score above 1.0
        // Math.Min(1.0f, ...) must cap it
        var recentCreate = DateTimeOffset.UtcNow.AddMinutes(-1);

        var result = MemoryDecayScoring.ApplyDecayWithUsage(
            score: 1.0f,
            createdAt: recentCreate,
            halfLifeDays: 7,
            accessCount: 10000,
            lastAccessedAt: recentCreate,
            usageBoostWeight: 1.0);

        result.ShouldBeLessThanOrEqualTo(1.0f,
            "score must be capped at 1.0 regardless of boost magnitude");
    }

    [Test]
    public void ApplyDecayWithUsage_ZeroUsageBoostWeight_SameAsApplyDecay()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-7);
        const float score = 1.0f;
        const double halfLife = 7;

        var withDecay = MemoryDecayScoring.ApplyDecay(score, createdAt, halfLife);
        var withUsage = MemoryDecayScoring.ApplyDecayWithUsage(
            score, createdAt, halfLife, accessCount: 100, usageBoostWeight: 0.0);

        withUsage.ShouldBe(withDecay, 0.001f,
            "zero boost weight must produce same result as plain decay");
    }

    // ── Large values ──────────────────────────────────────────────────

    [Test]
    public void ApplyDecay_VeryLargeHalfLife_ScoreNearlyUnchanged()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-3650); // 10 years old
        const double halfLifeDays = 1_000_000; // effectively immortal

        var result = MemoryDecayScoring.ApplyDecay(1.0f, createdAt, halfLifeDays);

        result.ShouldBeGreaterThan(0.99f,
            "with a massive half-life, even a 10-year-old fact barely decays");
    }
}
