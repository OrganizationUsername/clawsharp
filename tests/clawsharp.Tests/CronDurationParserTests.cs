using System.Reflection;
using Clawsharp.Cron;

namespace Clawsharp.Tests;

public sealed class CronDurationParserTests
{
    private static bool InvokeTryParseDuration(string input, out long ms)
    {
        var method = typeof(CronScheduleParser).GetMethod("TryParseDuration",
            BindingFlags.Public | BindingFlags.Static)!;
        var args = new object?[] { input, 0L };
        var result = (bool)method.Invoke(null, args)!;
        ms = (long)args[1]!;
        return result;
    }

    [TestCase("5m", true, 300_000L)]
    [TestCase("2h", true, 7_200_000L)]
    [TestCase("30s", true, 30_000L)]
    [TestCase("500ms", true, 500L)]
    [TestCase("1MS", true, 1L)]
    [TestCase("1H", true, 3_600_000L)]
    [TestCase("10M", true, 600_000L)]
    [TestCase("2000ms", true, 2000L)]
    public void TryParseDuration_ValidInput_ReturnsTrueWithCorrectMilliseconds(string input, bool expectedResult, long expectedMs)
    {
        var result = InvokeTryParseDuration(input, out var ms);

        result.ShouldBe(expectedResult);
        ms.ShouldBe(expectedMs);
    }

    [Test]
    public void TryParseDuration_BareNumber_TreatedAsSeconds()
    {
        var result = InvokeTryParseDuration("60", out var ms);

        result.ShouldBeTrue();
        ms.ShouldBe(60_000L);
    }

    [TestCase("0s")]
    [TestCase("0ms")]
    [TestCase("-1s")]
    [TestCase("")]
    [TestCase("abc")]
    [TestCase("5x")]
    public void TryParseDuration_InvalidInput_ReturnsFalse(string input)
    {
        InvokeTryParseDuration(input, out _).ShouldBeFalse();
    }
}