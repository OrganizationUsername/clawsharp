using Clawsharp.Cli.Config;

namespace Clawsharp.Tests.Unit.Cli;

/// <summary>Tests for <see cref="ConfigSetCommand.DetectTypedValue"/> type detection logic.</summary>
public sealed class ConfigSetCommandTests
{
    // ── Bool detection ──────────────────────────────────────────────────────

    [Test]
    public void DetectTypedValue_True_ReturnsBoolTrue()
    {
        var result = ConfigSetCommand.DetectTypedValue("true");

        result!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public void DetectTypedValue_TrueMixedCase_ReturnsBoolTrue()
    {
        var result = ConfigSetCommand.DetectTypedValue("True");

        result!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public void DetectTypedValue_TrueUpperCase_ReturnsBoolTrue()
    {
        var result = ConfigSetCommand.DetectTypedValue("TRUE");

        result!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public void DetectTypedValue_False_ReturnsBoolFalse()
    {
        var result = ConfigSetCommand.DetectTypedValue("false");

        result!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    public void DetectTypedValue_FalseUpperCase_ReturnsBoolFalse()
    {
        var result = ConfigSetCommand.DetectTypedValue("FALSE");

        result!.GetValue<bool>().ShouldBeFalse();
    }

    // ── Numeric strings stay as strings (L21 fix: prevents API key corruption) ──

    [Test]
    public void DetectTypedValue_IntegerString_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("123");

        result!.GetValue<string>().ShouldBe("123");
    }

    [Test]
    public void DetectTypedValue_Zero_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("0");

        result!.GetValue<string>().ShouldBe("0");
    }

    [Test]
    public void DetectTypedValue_NegativeInt_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("-42");

        result!.GetValue<string>().ShouldBe("-42");
    }

    [Test]
    public void DetectTypedValue_IntMaxValue_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("2147483647");

        result!.GetValue<string>().ShouldBe("2147483647");
    }

    [Test]
    public void DetectTypedValue_IntMinValue_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("-2147483648");

        result!.GetValue<string>().ShouldBe("-2147483648");
    }

    // ── String fallback ─────────────────────────────────────────────────────

    [Test]
    public void DetectTypedValue_PlainString_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("hello");

        result!.GetValue<string>().ShouldBe("hello");
    }

    [Test]
    public void DetectTypedValue_EmptyString_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("");

        result!.GetValue<string>().ShouldBe("");
    }

    [Test]
    public void DetectTypedValue_FloatingPoint_ReturnsString()
    {
        // "3.14" is not a valid int, so it falls through to string.
        var result = ConfigSetCommand.DetectTypedValue("3.14");

        result!.GetValue<string>().ShouldBe("3.14");
    }

    [Test]
    public void DetectTypedValue_LargeNumberBeyondInt32_ReturnsString()
    {
        // 123456789012345 exceeds int.MaxValue (2147483647), so int.TryParse fails
        // and it falls through to string.
        var result = ConfigSetCommand.DetectTypedValue("123456789012345");

        result!.GetValue<string>().ShouldBe("123456789012345");
    }

    [Test]
    public void DetectTypedValue_IntMaxValuePlusOne_ReturnsString()
    {
        // 2147483648 overflows int32, falls to string.
        var result = ConfigSetCommand.DetectTypedValue("2147483648");

        result!.GetValue<string>().ShouldBe("2147483648");
    }

    [Test]
    public void DetectTypedValue_ApiKeyString_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("sk-abc123xyz");

        result!.GetValue<string>().ShouldBe("sk-abc123xyz");
    }

    [Test]
    public void DetectTypedValue_EncryptedValue_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("enc2:AAAA");

        result!.GetValue<string>().ShouldBe("enc2:AAAA");
    }

    // ── Precedence: bool wins over int ──────────────────────────────────────

    [Test]
    public void DetectTypedValue_BoolTakesPrecedenceOverInt()
    {
        // "true" should never become an int -- bool check comes first.
        var result = ConfigSetCommand.DetectTypedValue("true");

        result!.GetValue<bool>().ShouldBeTrue();
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Test]
    public void DetectTypedValue_WhitespaceString_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("  ");

        result!.GetValue<string>().ShouldBe("  ");
    }

    [Test]
    public void DetectTypedValue_TelegramUserId_ReturnsString()
    {
        // Numeric strings stay as strings to prevent API key corruption (L21 fix).
        // Use --type int to force integer when needed.
        var result = ConfigSetCommand.DetectTypedValue("123456789");

        result!.GetValue<string>().ShouldBe("123456789");
    }

    [Test]
    public void DetectTypedValue_TrueWithLeadingSpace_ReturnsString()
    {
        // " true" is not "true" -- leading space means it falls to string.
        var result = ConfigSetCommand.DetectTypedValue(" true");

        result!.GetValue<string>().ShouldBe(" true");
    }

    // ── --type override: string ──────────────────────────────────────────

    [Test]
    public void DetectTypedValue_TypeString_NumericValue_ReturnsString()
    {
        // "123" would auto-detect as int, but --type string forces it to string.
        var result = ConfigSetCommand.DetectTypedValue("123", "string");

        result.ShouldNotBeNull();
        result!.GetValue<string>().ShouldBe("123");
    }

    [Test]
    public void DetectTypedValue_TypeString_BoolLikeValue_ReturnsString()
    {
        // "true" would auto-detect as bool, but --type string forces it to string.
        var result = ConfigSetCommand.DetectTypedValue("true", "string");

        result.ShouldNotBeNull();
        result!.GetValue<string>().ShouldBe("true");
    }

    [Test]
    public void DetectTypedValue_TypeString_PlainValue_ReturnsString()
    {
        var result = ConfigSetCommand.DetectTypedValue("hello", "string");

        result.ShouldNotBeNull();
        result!.GetValue<string>().ShouldBe("hello");
    }

    // ── --type override: int ─────────────────────────────────────────────

    [Test]
    public void DetectTypedValue_TypeInt_ValidNumber_ReturnsInt()
    {
        var result = ConfigSetCommand.DetectTypedValue("42", "int");

        result.ShouldNotBeNull();
        result!.GetValue<int>().ShouldBe(42);
    }

    [Test]
    public void DetectTypedValue_TypeInt_InvalidNumber_ReturnsNull()
    {
        var result = ConfigSetCommand.DetectTypedValue("not-a-number", "int");

        result.ShouldBeNull();
    }

    [Test]
    public void DetectTypedValue_TypeInt_FloatingPoint_ReturnsNull()
    {
        // "3.14" is not a valid int; --type int should fail.
        var result = ConfigSetCommand.DetectTypedValue("3.14", "int");

        result.ShouldBeNull();
    }

    // ── --type override: bool ────────────────────────────────────────────

    [Test]
    public void DetectTypedValue_TypeBool_TrueValue_ReturnsBool()
    {
        var result = ConfigSetCommand.DetectTypedValue("true", "bool");

        result.ShouldNotBeNull();
        result!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public void DetectTypedValue_TypeBool_FalseValue_ReturnsBool()
    {
        var result = ConfigSetCommand.DetectTypedValue("false", "bool");

        result.ShouldNotBeNull();
        result!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    public void DetectTypedValue_TypeBool_InvalidBool_ReturnsNull()
    {
        var result = ConfigSetCommand.DetectTypedValue("yes", "bool");

        result.ShouldBeNull();
    }

    // ── --type override: unrecognised ────────────────────────────────────

    [Test]
    public void DetectTypedValue_TypeUnrecognised_ReturnsNull()
    {
        var result = ConfigSetCommand.DetectTypedValue("hello", "float");

        result.ShouldBeNull();
    }

    // ── --type override: case insensitive ────────────────────────────────

    [Test]
    public void DetectTypedValue_TypeUpperCase_WorksCaseInsensitive()
    {
        var result = ConfigSetCommand.DetectTypedValue("42", "INT");

        result.ShouldNotBeNull();
        result!.GetValue<int>().ShouldBe(42);
    }
}