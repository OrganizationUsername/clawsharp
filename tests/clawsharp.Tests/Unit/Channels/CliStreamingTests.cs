namespace Clawsharp.Tests.Unit.Channels;

/// <summary>
/// Unit tests for CLI streaming token assembly behavior.
/// Verifies that the first token is trimmed but subsequent tokens preserve whitespace,
/// preventing the "Helloworld,howareyou?" bug where spaces between words are stripped.
/// </summary>
[TestFixture]
public sealed class CliStreamingTests
{
    /// <summary>
    /// Simulates the CliChannel.StreamAsync token assembly logic:
    /// first token is TrimStart()'d, all subsequent tokens are passed through unchanged.
    /// This is the core invariant that prevents the spacing bug.
    /// </summary>
    private static string AssembleTokens(params string[] tokens)
    {
        var sb = new System.Text.StringBuilder();
        var first = true;
        foreach (var token in tokens)
        {
            if (first)
            {
                sb.Append(token.TrimStart());
                first = false;
            }
            else
            {
                sb.Append(token);
            }
        }
        return sb.ToString();
    }

    [Test]
    public void AssembleTokens_PreservesSpacesBetweenWords()
    {
        var result = AssembleTokens("Hello", " world", " how", " are", " you", "?");
        result.ShouldBe("Hello world how are you?");
    }

    [Test]
    public void AssembleTokens_TrimsLeadingWhitespaceOnFirstTokenOnly()
    {
        var result = AssembleTokens("\n\nHello", " world");
        result.ShouldBe("Hello world");
    }

    [Test]
    public void AssembleTokens_PreservesLeadingSpaceOnSubsequentTokens()
    {
        var result = AssembleTokens("Hi", " there", "!", " How", " are", " you");
        result.ShouldBe("Hi there! How are you");
    }

    [Test]
    public void AssembleTokens_SingleToken_Trimmed()
    {
        var result = AssembleTokens("  Hello  ");
        result.ShouldBe("Hello  ");
    }

    [Test]
    public void AssembleTokens_EmptyFirstToken_TrimsToEmpty()
    {
        var result = AssembleTokens("", " Hello", " world");
        result.ShouldBe(" Hello world");
    }

    [Test]
    public void AssembleTokens_TypicalLlmStream_WithLeadingNewlines()
    {
        // LLMs often start streaming with "\n\n" followed by content tokens
        var result = AssembleTokens("\n\n", "The", " weather", " is", " nice", " today", ".");
        result.ShouldBe("The weather is nice today.");
    }

    [Test]
    public void AssembleTokens_NoTrimOnSecondToken_EvenIfAllWhitespace()
    {
        // A whitespace-only second token should NOT be trimmed
        var result = AssembleTokens("Hello", "   ", "world");
        result.ShouldBe("Hello   world");
    }

    [Test]
    public void AssembleTokens_RegressionTest_AllTokensTrimmed_ProducesConcatenatedWords()
    {
        // This is the bug we fixed: TrimStart on every token strips inter-word spaces
        var buggyResult = string.Concat(
            "Hello".TrimStart(), " world".TrimStart(), " how".TrimStart(), " are".TrimStart());
        buggyResult.ShouldBe("Helloworldhoware",
            "This demonstrates the bug when TrimStart is applied to all tokens");

        // The fixed behavior preserves spaces
        var fixedResult = AssembleTokens("Hello", " world", " how", " are");
        fixedResult.ShouldBe("Hello world how are");
    }
}
