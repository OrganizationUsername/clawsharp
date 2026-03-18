using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Providers;

[TestFixture]
public sealed class TagStripFilterEdgeCaseTests
{
    // ── Constructor validation ────────────────────────────────────────

    [Test]
    public void Constructor_EmptyTagNamesArray_ThrowsArgumentOutOfRange()
    {
        // ArgumentOutOfRangeException.ThrowIfZero(tagNames.Length)
        Should.Throw<ArgumentOutOfRangeException>(() => new TagStripFilter());
    }

    // ── StripTags static method ───────────────────────────────────────

    [Test]
    public void StripTags_EmptyText_ReturnsEmpty()
    {
        TagStripFilter.StripTags("", "think").ShouldBe("");
    }

    [Test]
    public void StripTags_NoMatchingTag_ReturnsUnchanged()
    {
        const string input = "Hello there!";
        TagStripFilter.StripTags(input, "think").ShouldBe(input);
    }

    [Test]
    public void StripTags_MatchingTag_Removed()
    {
        TagStripFilter.StripTags("<think>hidden</think>visible", "think")
            .ShouldBe("visible");
    }

    [Test]
    public void StripTags_SpecialRegexCharsInTagName_EscapedCorrectly()
    {
        // Tag name with chars that need regex escaping
        // This should not throw a regex exception
        Should.NotThrow(() =>
            TagStripFilter.StripTags("content", "think.me"));
    }

    // ── Streaming: nested angle brackets inside block ─────────────────

    [Test]
    public void ProcessChunk_MultipleAngleBracketsInsideBlock_AllSuppressed()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // Inside <think>, there are multiple < chars that are not closing tags
        var result = filter.ProcessChunk("<think>a < b && c > d</think>after");
        result.ShouldBe("after");
    }

    // ── Streaming: false close tag prefix then real close tag ─────────

    [Test]
    public void ProcessChunk_FalseCloseTagThenRealClose_HandledCorrectly()
    {
        var filter = TagStripFilter.CreateThinkingFilter();

        // "</thin" is a prefix of both </think> and </thinking>
        // but after "k" it's </think> — valid close for <think>
        var result = filter.ProcessChunk("<think>reasoning</thin" + "k>visible");
        result.ShouldBe("visible");
    }

    // ── Streaming: tool_call / tool_result filter ─────────────────────

    [Test]
    public void ProcessChunk_ToolCallBlock_Suppressed()
    {
        var filter = TagStripFilter.CreateStreamingFilter();
        var result = filter.ProcessChunk("<tool_call>call content</tool_call>after");
        result.ShouldBe("after");
    }

    [Test]
    public void ProcessChunk_ToolResultBlock_Suppressed()
    {
        var filter = TagStripFilter.CreateStreamingFilter();
        var result = filter.ProcessChunk("<tool_result>result content</tool_result>after");
        result.ShouldBe("after");
    }

    [Test]
    public void ProcessChunk_StreamingFilter_TextOutsideBlocks_PassesThrough()
    {
        var filter = TagStripFilter.CreateStreamingFilter();
        var result = filter.ProcessChunk("visible <tool_call>hidden</tool_call> also visible");
        result.ShouldBe("visible  also visible");
    }

    // ── StripThinkingBlocks handles null/empty ────────────────────────

    [Test]
    public void StripThinkingBlocks_Null_ReturnsNull()
    {
        TagStripFilter.StripThinkingBlocks(null!).ShouldBeNull();
    }

    [Test]
    public void StripToolTags_Null_ReturnsNull()
    {
        TagStripFilter.StripToolTags(null!).ShouldBeNull();
    }

    // ── ProcessChunk called with null (should return empty, not throw) ─

    [Test]
    public void ProcessChunk_NullChunk_ReturnsEmpty()
    {
        var filter = TagStripFilter.CreateThinkingFilter();
        var result = filter.ProcessChunk(null!);
        result.ShouldBe(string.Empty);
    }
}
