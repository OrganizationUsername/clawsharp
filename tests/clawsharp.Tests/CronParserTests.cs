using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Tests;

public sealed class CronParserTests
{
    // Tuesday, March 3, 2026, 09:30:00 UTC
    private static readonly DateTimeOffset Tuesday0930 = new(2026, 3, 3, 9, 30, 0, TimeSpan.Zero);

    // ── Wildcard ─────────────────────────────────────────────────────

    [Test]
    public void CronMatches_Wildcard_AlwaysReturnsTrue()
    {
        CronService.CronMatches("* * * * *", Tuesday0930).ShouldBeTrue();
    }

    // ── Exact minute/hour ────────────────────────────────────────────

    [Test]
    public void CronMatches_ExactMinuteHour_ReturnsTrue()
    {
        CronService.CronMatches("30 9 * * *", Tuesday0930).ShouldBeTrue();
    }

    [Test]
    public void CronMatches_WrongMinute_ReturnsFalse()
    {
        var time = new DateTimeOffset(2026, 3, 3, 9, 31, 0, TimeSpan.Zero);
        CronService.CronMatches("30 9 * * *", time).ShouldBeFalse();
    }

    [Test]
    public void CronMatches_WrongHour_ReturnsFalse()
    {
        var time = new DateTimeOffset(2026, 3, 3, 10, 30, 0, TimeSpan.Zero);
        CronService.CronMatches("30 9 * * *", time).ShouldBeFalse();
    }

    // ── Day-of-week range ────────────────────────────────────────────

    [Test]
    public void CronMatches_WeekdayRange_MatchesTuesday()
    {
        // Tuesday (DayOfWeek=2) is in range 1-5
        CronService.CronMatches("0 9 * * 1-5", Tuesday0930.AddMinutes(-30)).ShouldBeTrue();
    }

    [Test]
    public void CronMatches_WeekdayRange_DoesNotMatchSaturday()
    {
        // Saturday, March 7, 2026 at 09:00
        var saturday = new DateTimeOffset(2026, 3, 7, 9, 0, 0, TimeSpan.Zero);
        CronService.CronMatches("0 9 * * 1-5", saturday).ShouldBeFalse();
    }

    [Test]
    public void CronMatches_WeekdayRange_DoesNotMatchSunday()
    {
        // Sunday, March 1, 2026 at 09:00
        var sunday = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        CronService.CronMatches("0 9 * * 1-5", sunday).ShouldBeFalse();
    }

    // ── Exact day-of-month and month ─────────────────────────────────

    [Test]
    public void CronMatches_ExactDayAndMonth_MatchesJan1()
    {
        var jan1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        CronService.CronMatches("0 0 1 1 *", jan1).ShouldBeTrue();
    }

    [Test]
    public void CronMatches_ExactDayAndMonth_DoesNotMatchOtherDay()
    {
        CronService.CronMatches("0 0 1 1 *", Tuesday0930).ShouldBeFalse();
    }

    // ── Step values ──────────────────────────────────────────────────

    [TestCase(0, true)]
    [TestCase(15, true)]
    [TestCase(30, true)]
    [TestCase(45, true)]
    [TestCase(16, false)]
    [TestCase(31, false)]
    public void CronMatches_StepBy15_MatchesCorrectMinutes(int minute, bool expected)
    {
        var time = new DateTimeOffset(2026, 3, 3, 9, minute, 0, TimeSpan.Zero);
        CronService.CronMatches("*/15 * * * *", time).ShouldBe(expected);
    }

    // ── Hour range ───────────────────────────────────────────────────

    [TestCase(8, true)]
    [TestCase(12, true)]
    [TestCase(18, true)]
    [TestCase(7, false)]
    [TestCase(19, false)]
    public void CronMatches_HourRange_MatchesCorrectHours(int hour, bool expected)
    {
        var time = new DateTimeOffset(2026, 3, 3, hour, 0, 0, TimeSpan.Zero);
        CronService.CronMatches("0 8-18 * * *", time).ShouldBe(expected);
    }

    // ── Comma-separated values ───────────────────────────────────────

    [TestCase(0, true)]
    [TestCase(30, true)]
    [TestCase(15, false)]
    public void CronMatches_CommaSeparated_MatchesListedMinutes(int minute, bool expected)
    {
        var time = new DateTimeOffset(2026, 3, 3, 9, minute, 0, TimeSpan.Zero);
        CronService.CronMatches("0,30 * * * *", time).ShouldBe(expected);
    }

    // ── Step by 5 ────────────────────────────────────────────────────

    [TestCase(0, true)]
    [TestCase(5, true)]
    [TestCase(10, true)]
    [TestCase(15, true)]
    [TestCase(7, false)]
    public void CronMatches_StepBy5_MatchesCorrectMinutes(int minute, bool expected)
    {
        var time = new DateTimeOffset(2026, 3, 3, 9, minute, 0, TimeSpan.Zero);
        CronService.CronMatches("*/5 * * * *", time).ShouldBe(expected);
    }

    // ── Invalid field counts ─────────────────────────────────────────

    [Test]
    public void CronMatches_FourFields_ReturnsFalse()
    {
        CronService.CronMatches("* * * *", Tuesday0930).ShouldBeFalse();
    }

    [Test]
    public void CronMatches_SixFields_ReturnsFalse()
    {
        CronService.CronMatches("* * * * * *", Tuesday0930).ShouldBeFalse();
    }

    // ── Out-of-range values ──────────────────────────────────────────

    [Test]
    public void CronMatches_Minute60OutOfRange_NeverMatches()
    {
        // Minute 60 is out of range (0-59) — should not match any valid minute
        for (var m = 0; m < 60; m++)
        {
            var time = new DateTimeOffset(2026, 3, 3, 9, m, 0, TimeSpan.Zero);
            CronService.CronMatches("60 * * * *", time).ShouldBeFalse();
        }
    }

    // ── Specific day-of-week ─────────────────────────────────────────

    [Test]
    public void CronMatches_DayOfWeek6_MatchesSaturdayNotTuesday()
    {
        // Tuesday0930 is DayOfWeek=2 (Tuesday), should not match 6 (Saturday)
        CronService.CronMatches("0 9 * * 6", Tuesday0930.AddMinutes(-30)).ShouldBeFalse();

        // Saturday March 7, 2026 at 09:00
        var saturday = new DateTimeOffset(2026, 3, 7, 9, 0, 0, TimeSpan.Zero);
        CronService.CronMatches("0 9 * * 6", saturday).ShouldBeTrue();
    }

    [Test]
    public void CronMatches_DayOfWeek0_MatchesSunday()
    {
        // Sunday March 1, 2026 at 09:00
        var sunday = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        CronService.CronMatches("0 9 * * 0", sunday).ShouldBeTrue();
    }
}