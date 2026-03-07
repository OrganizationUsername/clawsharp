using Clawsharp.Channels.Slack;

namespace Clawsharp.Tests;

/// <summary>
/// Tests for Slack mrkdwn conversion and empty-text guard in SlackChannel.
/// Calls the internal static ConvertToMrkdwn method directly.
/// </summary>
public sealed class SlackMrkdwnTests
{
    // ── ConvertToMrkdwn: bold ────────────────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_Bold_DoubleAsterisksToSingle()
    {
        var result = SlackChannel.ConvertToMrkdwn("This is **bold** text");
        result.ShouldBe("This is *bold* text");
    }

    [Test]
    public void ConvertToMrkdwn_Bold_MultipleBoldSpans()
    {
        var result = SlackChannel.ConvertToMrkdwn("**one** and **two**");
        result.ShouldBe("*one* and *two*");
    }

    // ── ConvertToMrkdwn: italic ──────────────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_Italic_DoubleUnderscoreToSingle()
    {
        var result = SlackChannel.ConvertToMrkdwn("This is __italic__ text");
        result.ShouldBe("This is _italic_ text");
    }

    [Test]
    public void ConvertToMrkdwn_Italic_SingleUnderscoreUnchanged()
    {
        var result = SlackChannel.ConvertToMrkdwn("This is _italic_ text");
        result.ShouldBe("This is _italic_ text");
    }

    // ── ConvertToMrkdwn: strikethrough ───────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_Strikethrough_DoubleTildeToSingle()
    {
        var result = SlackChannel.ConvertToMrkdwn("This is ~~struck~~ text");
        result.ShouldBe("This is ~struck~ text");
    }

    // ── ConvertToMrkdwn: links ───────────────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_Link_MarkdownToSlackFormat()
    {
        var result = SlackChannel.ConvertToMrkdwn("Click [here](https://example.com) for info");
        result.ShouldBe("Click <https://example.com|here> for info");
    }

    [Test]
    public void ConvertToMrkdwn_Link_MultipleLinks()
    {
        var result = SlackChannel.ConvertToMrkdwn("[a](http://a.com) and [b](http://b.com)");
        result.ShouldBe("<http://a.com|a> and <http://b.com|b>");
    }

    // ── ConvertToMrkdwn: headers ─────────────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_Header_H1ToBold()
    {
        var result = SlackChannel.ConvertToMrkdwn("# Heading One");
        result.ShouldBe("*Heading One*");
    }

    [Test]
    public void ConvertToMrkdwn_Header_H3ToBold()
    {
        var result = SlackChannel.ConvertToMrkdwn("### Heading Three");
        result.ShouldBe("*Heading Three*");
    }

    [Test]
    public void ConvertToMrkdwn_Header_MultilineHeaders()
    {
        var input = "# Title\nSome text\n## Subtitle";
        var result = SlackChannel.ConvertToMrkdwn(input);
        result.ShouldBe("*Title*\nSome text\n*Subtitle*");
    }

    // ── ConvertToMrkdwn: unordered lists ─────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_UnorderedList_DashToBullet()
    {
        var input = "Items:\n- first\n- second";
        var result = SlackChannel.ConvertToMrkdwn(input);
        result.ShouldBe("Items:\n\u2022 first\n\u2022 second");
    }

    [Test]
    public void ConvertToMrkdwn_UnorderedList_AsteriskToBullet()
    {
        var input = "Items:\n* first\n* second";
        var result = SlackChannel.ConvertToMrkdwn(input);
        result.ShouldBe("Items:\n\u2022 first\n\u2022 second");
    }

    // ── ConvertToMrkdwn: code preservation ───────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_CodeBlock_Unchanged()
    {
        var input = "Before\n```\n**not bold**\n~~not struck~~\n```\nAfter";
        var result = SlackChannel.ConvertToMrkdwn(input);
        // Code block content must not be converted
        result.ShouldContain("**not bold**");
        result.ShouldContain("~~not struck~~");
        result.ShouldStartWith("Before\n");
        result.ShouldEndWith("\nAfter");
    }

    [Test]
    public void ConvertToMrkdwn_InlineCode_Unchanged()
    {
        var result = SlackChannel.ConvertToMrkdwn("Use `**literal**` here");
        result.ShouldBe("Use `**literal**` here");
    }

    [Test]
    public void ConvertToMrkdwn_CodeBlock_LanguageTag_Unchanged()
    {
        var input = "```csharp\nvar x = **y**;\n```";
        var result = SlackChannel.ConvertToMrkdwn(input);
        result.ShouldBe("```csharp\nvar x = **y**;\n```");
    }

    // ── ConvertToMrkdwn: combined ────────────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_Combined_AllFormats()
    {
        var input = "# Title\n**bold** and ~~struck~~ with [link](http://x.com)\n- item";
        var result = SlackChannel.ConvertToMrkdwn(input);
        result.ShouldBe("*Title*\n*bold* and ~struck~ with <http://x.com|link>\n\u2022 item");
    }

    // ── ConvertToMrkdwn: edge cases ──────────────────────────────────────────

    [Test]
    public void ConvertToMrkdwn_EmptyString_ReturnsEmpty()
    {
        SlackChannel.ConvertToMrkdwn("").ShouldBe("");
    }

    [Test]
    public void ConvertToMrkdwn_Null_ReturnsNull()
    {
        SlackChannel.ConvertToMrkdwn(null!).ShouldBeNull();
    }

    [Test]
    public void ConvertToMrkdwn_PlainText_Unchanged()
    {
        var result = SlackChannel.ConvertToMrkdwn("just plain text");
        result.ShouldBe("just plain text");
    }

    [Test]
    public void ConvertToMrkdwn_SingleAsterisk_NotBold_Unchanged()
    {
        // Single asterisks should not be affected by the bold conversion
        var result = SlackChannel.ConvertToMrkdwn("use *italic* in slack");
        result.ShouldBe("use *italic* in slack");
    }

    [Test]
    public void ConvertToMrkdwn_BoldAndItalicMixed()
    {
        var result = SlackChannel.ConvertToMrkdwn("**bold** and _italic_");
        result.ShouldBe("*bold* and _italic_");
    }

    [Test]
    public void ConvertToMrkdwn_HeaderInsideCodeBlock_NotConverted()
    {
        var input = "```\n# Not a heading\n```";
        var result = SlackChannel.ConvertToMrkdwn(input);
        result.ShouldBe("```\n# Not a heading\n```");
    }

    [Test]
    public void ConvertToMrkdwn_ListItemNotAtLineStart_Unchanged()
    {
        // "- " only at line start should be converted; mid-line dashes are fine
        var result = SlackChannel.ConvertToMrkdwn("a - b");
        result.ShouldBe("a - b");
    }

    [Test]
    public void ConvertToMrkdwn_NestedBold_ConvertedCorrectly()
    {
        var result = SlackChannel.ConvertToMrkdwn("Text **bold with _italic_ inside** end");
        result.ShouldBe("Text *bold with _italic_ inside* end");
    }
}