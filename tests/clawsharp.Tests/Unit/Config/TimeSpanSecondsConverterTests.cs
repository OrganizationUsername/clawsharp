using System.Text.Json;
using Clawsharp.Config;

namespace Clawsharp.Tests.Unit.Config;

/// <summary>
/// Tests for <see cref="TimeSpanSecondsConverter"/> which provides backward compatibility
/// for config properties migrated from int/double seconds to TimeSpan.
/// </summary>
[TestFixture]
public sealed class TimeSpanSecondsConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TimeSpanSecondsConverter() }
    };

    // -- Read: integer seconds --

    [Test]
    public void Read_IntegerValue_ConvertsToTimeSpanFromSeconds()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("60", Options);
        result.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Test]
    public void Read_ZeroInteger_ReturnsZeroTimeSpan()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("0", Options);
        result.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void Read_LargeInteger_ConvertsCorrectly()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("3600", Options);
        result.ShouldBe(TimeSpan.FromHours(1));
    }

    // -- Read: double seconds --

    [Test]
    public void Read_DoubleValue_ConvertsToTimeSpanFromSeconds()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("1.5", Options);
        result.ShouldBe(TimeSpan.FromSeconds(1.5));
    }

    [Test]
    public void Read_SmallDouble_ConvertsToSubSecondTimeSpan()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("0.5", Options);
        result.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    // -- Read: string TimeSpan format --

    [Test]
    public void Read_TimeSpanString_ParsesCorrectly()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("\"00:01:00\"", Options);
        result.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Test]
    public void Read_TimeSpanStringWithDays_ParsesCorrectly()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("\"1.00:00:00\"", Options);
        result.ShouldBe(TimeSpan.FromDays(1));
    }

    [Test]
    public void Read_TimeSpanStringWithMilliseconds_ParsesCorrectly()
    {
        var result = JsonSerializer.Deserialize<TimeSpan>("\"00:00:05.500\"", Options);
        result.ShouldBe(TimeSpan.FromSeconds(5.5));
    }

    // -- Read: error cases --

    [Test]
    public void Read_BooleanToken_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>("true", Options));
    }

    [Test]
    public void Read_ArrayToken_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>("[]", Options));
    }

    [Test]
    public void Read_ObjectToken_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>("{}", Options));
    }

    [Test]
    public void Read_InvalidTimeSpanString_ThrowsException()
    {
        Should.Throw<Exception>(() => JsonSerializer.Deserialize<TimeSpan>("\"not-a-timespan\"", Options));
    }

    // -- Write --

    [Test]
    public void Write_TimeSpan_WritesAsString()
    {
        var json = JsonSerializer.Serialize(TimeSpan.FromMinutes(5), Options);
        json.ShouldBe("\"00:05:00\"");
    }

    [Test]
    public void Write_ZeroTimeSpan_WritesAsZeroString()
    {
        var json = JsonSerializer.Serialize(TimeSpan.Zero, Options);
        json.ShouldBe("\"00:00:00\"");
    }

    // -- Round-trip with config object --

    private sealed record TimeSpanHolder(
        [property: System.Text.Json.Serialization.JsonConverter(typeof(TimeSpanSecondsConverter))]
        TimeSpan Value
    );

    [Test]
    public void RoundTrip_IntegerSeconds_DeserializesAndSerializesAsString()
    {
        var holder = JsonSerializer.Deserialize<TimeSpanHolder>("""{"Value":120}""");
        holder.ShouldNotBeNull();
        holder.Value.ShouldBe(TimeSpan.FromSeconds(120));

        var json = JsonSerializer.Serialize(holder);
        json.ShouldContain("\"00:02:00\"");
    }

    [Test]
    public void RoundTrip_StringTimeSpan_PreservesValue()
    {
        var holder = JsonSerializer.Deserialize<TimeSpanHolder>("""{"Value":"00:05:00"}""");
        holder.ShouldNotBeNull();
        holder.Value.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
