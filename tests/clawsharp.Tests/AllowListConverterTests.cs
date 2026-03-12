using System.Text.Json;
using Clawsharp.Config.Channels;

namespace Clawsharp.Tests;

public sealed class AllowListConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new AllowListConverter() }
    };

    private static List<string>? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<List<string>?>(json, Options);
    }

    private static string Serialize(List<string>? value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    // ── Read tests ───────────────────────────────────────────────────

    [Test]
    public void Read_Null_ReturnsNull()
    {
        Deserialize("null").ShouldBeNull();
    }

    [Test]
    public void Read_EmptyArray_ReturnsEmptyList()
    {
        var result = Deserialize("[]");

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Test]
    public void Read_StringArray_ReturnsSameStrings()
    {
        var result = Deserialize("""["@alice", "@bob"]""");

        result.ShouldNotBeNull();
        result.ShouldBe(["@alice", "@bob"]);
    }

    [Test]
    public void Read_NumericArray_ConvertsToStrings()
    {
        var result = Deserialize("[123456789, 987654321]");

        result.ShouldNotBeNull();
        result.ShouldBe(["123456789", "987654321"]);
    }

    [Test]
    public void Read_MixedArray_ConvertsAllToStrings()
    {
        var result = Deserialize("""["@alice", 123456789]""");

        result.ShouldNotBeNull();
        result.ShouldBe(["@alice", "123456789"]);
    }

    [Test]
    public void Read_Wildcard_ReturnsWildcard()
    {
        var result = Deserialize("""["*"]""");

        result.ShouldNotBeNull();
        result.ShouldBe(["*"]);
    }

    [Test]
    public void Read_NonArrayToken_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => Deserialize(""""string_value""""));
    }

    [Test]
    public void Read_ArrayWithBoolean_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => Deserialize("[true]"));
    }

    // ── Write tests ──────────────────────────────────────────────────

    [Test]
    public void Write_Null_WritesNull()
    {
        Serialize(null).ShouldBe("null");
    }

    [Test]
    public void Write_StringList_WritesStringArray()
    {
        Serialize(["@alice"]).ShouldBe("""["@alice"]""");
    }

    [Test]
    public void Write_NumericStringList_WritesAsStrings()
    {
        // Should be written as a string, not a number
        Serialize(["123456789"]).ShouldBe("""["123456789"]""");
    }
}