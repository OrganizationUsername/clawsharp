using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

[TestFixture]
public sealed class SuspicionTrackerTests
{
    // ── Initial state ─────────────────────────────────────────────────

    [Test]
    public void InitialState_ScoreIsZero()
    {
        var tracker = new SuspicionTracker();
        tracker.Score.ShouldBe(0);
    }

    [Test]
    public void InitialState_IsNotWarning()
    {
        var tracker = new SuspicionTracker();
        tracker.IsWarning.ShouldBeFalse();
    }

    [Test]
    public void InitialState_IsNotBlocked()
    {
        var tracker = new SuspicionTracker();
        tracker.IsBlocked.ShouldBeFalse();
    }

    // ── WarnThreshold exact boundary ─────────────────────────────────

    [Test]
    public void RecordSuspicion_ScoreOneBelowWarnThreshold_NotWarning()
    {
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(SuspicionTracker.WarnThreshold - 1);

        tracker.Score.ShouldBe(SuspicionTracker.WarnThreshold - 1);
        tracker.IsWarning.ShouldBeFalse(
            $"score of {SuspicionTracker.WarnThreshold - 1} is below WarnThreshold={SuspicionTracker.WarnThreshold}");
    }

    [Test]
    public void RecordSuspicion_ScoreExactlyAtWarnThreshold_IsWarning()
    {
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(SuspicionTracker.WarnThreshold);

        tracker.IsWarning.ShouldBeTrue(
            $"score of {SuspicionTracker.WarnThreshold} equals WarnThreshold — must be warning");
        tracker.IsBlocked.ShouldBeFalse("warn threshold must not trigger block");
    }

    [Test]
    public void RecordSuspicion_OneAboveWarnThreshold_StillWarningNotBlocked()
    {
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(SuspicionTracker.WarnThreshold + 1);

        tracker.IsWarning.ShouldBeTrue();
        tracker.IsBlocked.ShouldBeFalse(
            $"score of {SuspicionTracker.WarnThreshold + 1} is below BlockThreshold={SuspicionTracker.BlockThreshold}");
    }

    // ── BlockThreshold exact boundary ────────────────────────────────

    [Test]
    public void RecordSuspicion_ScoreOneBelowBlockThreshold_NotBlocked()
    {
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold - 1);

        tracker.IsBlocked.ShouldBeFalse(
            $"score of {SuspicionTracker.BlockThreshold - 1} is below BlockThreshold={SuspicionTracker.BlockThreshold}");
        tracker.IsWarning.ShouldBeTrue("score is still >= WarnThreshold");
    }

    [Test]
    public void RecordSuspicion_ScoreExactlyAtBlockThreshold_IsBlocked()
    {
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold);

        tracker.IsBlocked.ShouldBeTrue(
            $"score of {SuspicionTracker.BlockThreshold} equals BlockThreshold — must be blocked");
        tracker.IsWarning.ShouldBeTrue("block implies warn is also true");
    }

    // ── Incremental accumulation ──────────────────────────────────────

    [Test]
    public void RecordSuspicion_MultipleOnePointCalls_AccumulatesCorrectly()
    {
        var tracker = new SuspicionTracker();

        for (var i = 0; i < SuspicionTracker.WarnThreshold; i++)
        {
            tracker.RecordSuspicion(1);
        }

        tracker.Score.ShouldBe(SuspicionTracker.WarnThreshold);
        tracker.IsWarning.ShouldBeTrue();
    }

    [Test]
    public void RecordSuspicion_TwoPointCalls_CountsAsIndirectPattern()
    {
        // Indirect injection patterns add 2 points per detection
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(2); // one indirect detection
        tracker.RecordSuspicion(2); // second indirect detection

        tracker.Score.ShouldBe(4);
        tracker.IsWarning.ShouldBeTrue();
    }

    // ── Zero-point recording ──────────────────────────────────────────

    [Test]
    public void RecordSuspicion_ZeroPoints_ScoreUnchanged()
    {
        var tracker = new SuspicionTracker();

        tracker.RecordSuspicion(0);

        tracker.Score.ShouldBe(0);
        tracker.IsWarning.ShouldBeFalse();
    }

    // ── Reset ─────────────────────────────────────────────────────────

    [Test]
    public void Reset_AfterAccumulation_ScoreBecomesZero()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(SuspicionTracker.BlockThreshold);
        tracker.IsBlocked.ShouldBeTrue();

        tracker.Reset();

        tracker.Score.ShouldBe(0);
        tracker.IsWarning.ShouldBeFalse();
        tracker.IsBlocked.ShouldBeFalse();
    }

    [Test]
    public void Reset_ThenAccumulate_WorksFromZero()
    {
        var tracker = new SuspicionTracker();
        tracker.RecordSuspicion(10);
        tracker.Reset();

        tracker.RecordSuspicion(1);

        tracker.Score.ShouldBe(1);
        tracker.IsWarning.ShouldBeFalse();
    }

    // ── Concurrency ───────────────────────────────────────────────────

    [Test]
    public async Task RecordSuspicion_ConcurrentCalls_ScoreNeverNegative()
    {
        var tracker = new SuspicionTracker();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var recordTasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            try { tracker.RecordSuspicion(1); }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        var resetTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try { tracker.Reset(); }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        await Task.WhenAll(recordTasks.Concat(resetTasks));

        exceptions.ShouldBeEmpty("Interlocked operations must never throw");
        tracker.Score.ShouldBeGreaterThanOrEqualTo(0,
            "score must never go negative regardless of interleaving");
    }

    [Test]
    public async Task RecordSuspicion_ConcurrentHighLoad_NoExceptions()
    {
        var tracker = new SuspicionTracker();
        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                tracker.RecordSuspicion(1);
            }
        }));

        Should.NotThrow(() => Task.WhenAll(tasks).GetAwaiter().GetResult());
        tracker.Score.ShouldBe(5000);
    }

    // ── Threshold constants are documented ────────────────────────────

    [Test]
    public void WarnThreshold_IsThree()
    {
        // Regression test: threshold is a documented contract, not an implementation detail.
        // Changing this value changes behavior visible to users (when warning is logged).
        SuspicionTracker.WarnThreshold.ShouldBe(3);
    }

    [Test]
    public void BlockThreshold_IsSix()
    {
        SuspicionTracker.BlockThreshold.ShouldBe(6);
    }

    [Test]
    public void BlockThreshold_IsGreaterThanWarnThreshold()
    {
        SuspicionTracker.BlockThreshold.ShouldBeGreaterThan(SuspicionTracker.WarnThreshold);
    }
}
