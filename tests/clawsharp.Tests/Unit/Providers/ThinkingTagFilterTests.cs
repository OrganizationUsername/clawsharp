using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Unit tests for <see cref="TagStripFilter"/>.
/// Verifies both the static <see cref="TagStripFilter.StripThinkingBlocks"/> method
/// and the streaming state machine (<see cref="TagStripFilter.ProcessChunk"/>/<see cref="TagStripFilter.Flush"/>).
/// </summary>
[TestFixture]
public sealed class TagStripFilterTests
{
    // =========================================================================
    // 1. Static StripThinkingBlocks
    // =========================================================================

    [Test]
    public void StripThinkingBlocks_NoTags_ReturnsUnchanged()
    {
        const string input = "Hello, this is a normal response.";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo(input));
    }

    [Test]
    public void StripThinkingBlocks_ThinkTag_Removed()
    {
        const string input = "<think>reasoning here</think>Visible answer.";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("Visible answer."));
    }

    [Test]
    public void StripThinkingBlocks_ThinkingTag_Removed()
    {
        const string input = "<thinking>some chain of thought</thinking>The real answer.";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("The real answer."));
    }

    [Test]
    public void StripThinkingBlocks_MultilineContent_Removed()
    {
        const string input = "<think>\nLine 1\nLine 2\nLine 3\n</think>Result.";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("Result."));
    }

    [Test]
    public void StripThinkingBlocks_MultipleBlocks_AllRemoved()
    {
        const string input = "<think>first</think>A<thinking>second</thinking>B";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("AB"));
    }

    [Test]
    public void StripThinkingBlocks_BlockInMiddle_SurroundingTextPreserved()
    {
        const string input = "Before <think>hidden</think> After";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("Before  After"));
    }

    [Test]
    public void StripThinkingBlocks_EmptyBlock_TagsRemoved()
    {
        const string input = "<think></think>Visible.";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("Visible."));
    }

    [TestCase(null)]
    [TestCase("")]
    public void StripThinkingBlocks_NullOrEmpty_ReturnsSame(string? input)
    {
        Assert.That(TagStripFilter.StripThinkingBlocks(input!), Is.EqualTo(input));
    }

    [Test]
    public void StripThinkingBlocks_OnlyThinkBlock_ReturnsEmpty()
    {
        const string input = "<think>everything is reasoning</think>";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo(string.Empty));
    }

    [Test]
    public void StripThinkingBlocks_NestedAngleBrackets_InnerNotConfused()
    {
        // A think block containing other HTML-like content
        const string input = "<think>I should use <b>bold</b> text</think>Answer.";
        Assert.That(TagStripFilter.StripThinkingBlocks(input), Is.EqualTo("Answer."));
    }

    // =========================================================================
    // 2. Streaming — Single-Chunk Scenarios
    // =========================================================================

    [Test]
    public void ProcessChunk_NoTags_PassesThrough()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        Assert.That(filter.ProcessChunk("Hello world"), Is.EqualTo("Hello world"));
    }

    [Test]
    public void ProcessChunk_CompleteThinkBlock_Suppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        var result = filter.ProcessChunk("<think>reasoning</think>Answer.");
        Assert.That(result, Is.EqualTo("Answer."));
    }

    [Test]
    public void ProcessChunk_CompleteThinkingBlock_Suppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        var result = filter.ProcessChunk("<thinking>reasoning</thinking>Answer.");
        Assert.That(result, Is.EqualTo("Answer."));
    }

    [Test]
    public void ProcessChunk_Empty_ReturnsEmpty()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        Assert.That(filter.ProcessChunk(""), Is.EqualTo(string.Empty));
    }

    // =========================================================================
    // 3. Streaming — Multi-Chunk Tag Splitting
    // =========================================================================

    [Test]
    public void ProcessChunk_OpenTagSplitAcrossTwoChunks()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // "<thi" is buffered
        var r1 = filter.ProcessChunk("Hello <thi");
        Assert.That(r1, Is.EqualTo("Hello "));

        // "nk>" completes the tag — enters suppression
        var r2 = filter.ProcessChunk("nk>reasoning</think>visible");
        Assert.That(r2, Is.EqualTo("visible"));
    }

    [Test]
    public void ProcessChunk_OpenTagSplitAcrossThreeChunks()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("<th");
        Assert.That(r1, Is.EqualTo(string.Empty));

        var r2 = filter.ProcessChunk("ink");
        Assert.That(r2, Is.EqualTo(string.Empty));

        var r3 = filter.ProcessChunk(">hidden</think>visible");
        Assert.That(r3, Is.EqualTo("visible"));
    }

    [Test]
    public void ProcessChunk_CloseTagSplitAcrossChunks()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("<think>reasoning</thi");
        Assert.That(r1, Is.EqualTo(string.Empty));

        var r2 = filter.ProcessChunk("nk>After.");
        Assert.That(r2, Is.EqualTo("After."));
    }

    [Test]
    public void ProcessChunk_ThinkingTagSplitAcrossChunks()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("<thinking");
        Assert.That(r1, Is.EqualTo(string.Empty));

        var r2 = filter.ProcessChunk(">some thought</thinking>result");
        Assert.That(r2, Is.EqualTo("result"));
    }

    [Test]
    public void ProcessChunk_CloseThinkingTagSplitAcrossChunks()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("<thinking>thought</thinki");
        Assert.That(r1, Is.EqualTo(string.Empty));

        var r2 = filter.ProcessChunk("ng>done");
        Assert.That(r2, Is.EqualTo("done"));
    }

    [Test]
    public void ProcessChunk_TextBeforeAndAfterBlock_MultiChunk()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("Before ");
        Assert.That(r1, Is.EqualTo("Before "));

        var r2 = filter.ProcessChunk("<think>hidden");
        Assert.That(r2, Is.EqualTo(string.Empty));

        var r3 = filter.ProcessChunk("</think>");
        Assert.That(r3, Is.EqualTo(string.Empty));

        var r4 = filter.ProcessChunk(" After");
        Assert.That(r4, Is.EqualTo(" After"));
    }

    // =========================================================================
    // 4. Streaming — Flush Behavior
    // =========================================================================

    [Test]
    public void Flush_NoBuffered_ReturnsEmpty()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        filter.ProcessChunk("Hello");
        Assert.That(filter.Flush(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Flush_PartialOpenTag_ReturnsBufferedText()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // "<th" could be start of <think> but stream ends
        filter.ProcessChunk("Hello <th");
        var flushed = filter.Flush();

        // The partial "<th" is not a think tag — return it as content
        Assert.That(flushed, Is.EqualTo("<th"));
    }

    [Test]
    public void Flush_InsideBlock_DiscardsContent()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // Opens a think block but stream ends before close
        filter.ProcessChunk("<think>reasoning without end");
        var flushed = filter.Flush();

        Assert.That(flushed, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Flush_PartialCloseTag_DiscardsContent()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        filter.ProcessChunk("<think>reasoning</thi");
        var flushed = filter.Flush();

        Assert.That(flushed, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Flush_ResetsState_CanBeReused()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        filter.ProcessChunk("<th");
        filter.Flush();

        // After flush, filter should be in Normal state
        var result = filter.ProcessChunk("Hello");
        Assert.That(result, Is.EqualTo("Hello"));
    }

    // =========================================================================
    // 5. Streaming — False Positives (angle brackets that are NOT think tags)
    // =========================================================================

    [Test]
    public void ProcessChunk_AngleBracketNotThinkTag_PassesThrough()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // "<b>" doesn't match any think tag prefix after "<"
        var result = filter.ProcessChunk("<b>bold</b>");
        Assert.That(result, Is.EqualTo("<b>bold</b>"));
    }

    [Test]
    public void ProcessChunk_LessThanFollowedBySpace_PassesThrough()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        var result = filter.ProcessChunk("1 < 2 and 3 > 1");
        Assert.That(result, Is.EqualTo("1 < 2 and 3 > 1"));
    }

    [Test]
    public void ProcessChunk_PartialThinkPrefix_ThenMismatch_FlushesAsText()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // "<thin" is a valid prefix of <think> and <thinking>
        // but then "g" followed by a space would break the prefix check
        // Actually "<thing" — "g" makes it "<thing" which is still prefix of "<thinking>"
        // Let's use "<thx" which breaks immediately
        var result = filter.ProcessChunk("<thx>stuff");
        Assert.That(result, Is.EqualTo("<thx>stuff"));
    }

    [Test]
    public void ProcessChunk_PartialPrefixSplitAcrossChunks_ThenMismatch()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("A<th");
        Assert.That(r1, Is.EqualTo("A"));

        // "e" makes it "<the" which is not a prefix of <think> or <thinking>
        var r2 = filter.ProcessChunk("e tag>");
        Assert.That(r2, Is.EqualTo("<the tag>"));
    }

    // =========================================================================
    // 6. Streaming — Multiple Blocks in Sequence
    // =========================================================================

    [Test]
    public void ProcessChunk_TwoConsecutiveBlocks_BothSuppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("<think>first</think>A<thinking>second</thinking>B");
        Assert.That(r1, Is.EqualTo("AB"));
    }

    [Test]
    public void ProcessChunk_TwoBlocksAcrossChunks()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("<think>first</think>Between");
        Assert.That(r1, Is.EqualTo("Between"));

        var r2 = filter.ProcessChunk("<thinking>second</thinking>End");
        Assert.That(r2, Is.EqualTo("End"));
    }

    // =========================================================================
    // 7. Streaming — Character-at-a-Time (Worst Case)
    // =========================================================================

    [Test]
    public void ProcessChunk_CharByChar_ThinkBlock()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        const string input = "<think>hidden</think>visible";
        var output = new System.Text.StringBuilder();

        foreach (var ch in input)
        {
            output.Append(filter.ProcessChunk(ch.ToString()));
        }

        output.Append(filter.Flush());
        Assert.That(output.ToString(), Is.EqualTo("visible"));
    }

    [Test]
    public void ProcessChunk_CharByChar_ThinkingBlock()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        const string input = "Before<thinking>hidden\nstuff</thinking>After";
        var output = new System.Text.StringBuilder();

        foreach (var ch in input)
        {
            output.Append(filter.ProcessChunk(ch.ToString()));
        }

        output.Append(filter.Flush());
        Assert.That(output.ToString(), Is.EqualTo("BeforeAfter"));
    }

    // =========================================================================
    // 8. Streaming — Edge Cases
    // =========================================================================

    [Test]
    public void ProcessChunk_EmptyThinkBlock_Suppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        var result = filter.ProcessChunk("<think></think>text");
        Assert.That(result, Is.EqualTo("text"));
    }

    [Test]
    public void ProcessChunk_BlockAtEndOfStream_Suppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        var r1 = filter.ProcessChunk("Answer<think>trailing thought");
        Assert.That(r1, Is.EqualTo("Answer"));

        // Stream ends without closing tag — flush discards
        var flushed = filter.Flush();
        Assert.That(flushed, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ProcessChunk_MultipleLessThanInsideBlock_Handled()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // Inside the block, encountering '<' that isn't the start of </think>
        var result = filter.ProcessChunk("<think>if x < 5 and y < 10 then</think>answer");
        Assert.That(result, Is.EqualTo("answer"));
    }

    [Test]
    public void ProcessChunk_FalseCloseTagPrefixInsideBlock_StaysInBlock()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // "</thin" starts buffering as potential close tag, then "g" extends it
        // but the actual close tag is </think>, not </thinking> (because we opened with <think>)
        // So "</thinking>" is NOT a match for a <think> block close.
        // Wait -- actually this IS tricky. Let's test the matched-tag pairing.
        var result = filter.ProcessChunk("<think>reasoning</thinking>still hidden</think>visible");
        Assert.That(result, Is.EqualTo("visible"));
    }

    [Test]
    public void ProcessChunk_ThinkingCloseDoesNotCloseThinkOpen()
    {
        // <think> must close with </think>, not </thinking>
        var filter = TagStripFilter.CreateThinkingFilter();
        var r1 = filter.ProcessChunk("<think>reason</thinking>");
        Assert.That(r1, Is.EqualTo(string.Empty)); // Still inside block

        var r2 = filter.ProcessChunk("still hidden</think>visible");
        Assert.That(r2, Is.EqualTo("visible"));
    }

    [Test]
    public void ProcessChunk_ThinkCloseDoesNotCloseThinkingOpen()
    {
        // <thinking> must close with </thinking>, not </think>
        var filter = TagStripFilter.CreateThinkingFilter();
        var r1 = filter.ProcessChunk("<thinking>reason</think>");
        Assert.That(r1, Is.EqualTo(string.Empty)); // Still inside block

        var r2 = filter.ProcessChunk("still hidden</thinking>visible");
        Assert.That(r2, Is.EqualTo("visible"));
    }

    [Test]
    public void ProcessChunk_BlockWithNewlines_Suppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        var result = filter.ProcessChunk("<think>\nline1\nline2\n</think>result");
        Assert.That(result, Is.EqualTo("result"));
    }

    [Test]
    public void ProcessChunk_OnlyOpenTagNoClose_FlushDiscards()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        filter.ProcessChunk("<think>");
        filter.ProcessChunk("reasoning that never ends");
        Assert.That(filter.Flush(), Is.EqualTo(string.Empty));
    }

    // =========================================================================
    // 9. Streaming — DeepSeek-R1 Realistic Pattern
    // =========================================================================

    [Test]
    public void ProcessChunk_DeepSeekR1Pattern_ThinkAtStart()
    {
        // DeepSeek-R1 typically emits <think> at the very start of the response
        var filter = TagStripFilter.CreateThinkingFilter();
        var output = new System.Text.StringBuilder();

        // Typical chunk sequence from DeepSeek
        output.Append(filter.ProcessChunk("<think>"));
        output.Append(filter.ProcessChunk("Let me analyze this problem step by step.\n"));
        output.Append(filter.ProcessChunk("First, I need to understand what the user wants.\n"));
        output.Append(filter.ProcessChunk("They want to know about X.\n"));
        output.Append(filter.ProcessChunk("</think>"));
        output.Append(filter.ProcessChunk("\n"));
        output.Append(filter.ProcessChunk("Here is the answer about X."));

        Assert.That(output.ToString(), Is.EqualTo("\nHere is the answer about X."));
    }

    [Test]
    public void ProcessChunk_QwenPattern_ThinkingSplitAcrossBoundaries()
    {
        // Qwen uses <thinking>...</thinking>
        var filter = TagStripFilter.CreateThinkingFilter();
        var output = new System.Text.StringBuilder();

        output.Append(filter.ProcessChunk("<thinki"));
        output.Append(filter.ProcessChunk("ng>"));
        output.Append(filter.ProcessChunk("The user wants to know about Y.\n"));
        output.Append(filter.ProcessChunk("Let me think...\n"));
        output.Append(filter.ProcessChunk("</thinki"));
        output.Append(filter.ProcessChunk("ng>"));
        output.Append(filter.ProcessChunk("The answer is Y."));

        Assert.That(output.ToString(), Is.EqualTo("The answer is Y."));
    }
}