using Clawsharp.Channels;

namespace Clawsharp.Tests.Channels;

public sealed class MessageChunkerTests
{
    // ── Basic splitting ────────────────────────────────────────────────

    [Test]
    public void Split_ShortText_ReturnsSingleChunk()
    {
        var chunks = MessageChunker.Split("Hello, world!", 100).ToList();

        chunks.Count.ShouldBe(1);
        chunks[0].ShouldBe("Hello, world!");
    }

    [Test]
    public void Split_TextExactlyAtLimit_ReturnsSingleChunk()
    {
        var text = new string('a', 50);

        var chunks = MessageChunker.Split(text, 50).ToList();

        chunks.Count.ShouldBe(1);
        chunks[0].ShouldBe(text);
    }

    [Test]
    public void Split_TextSlightlyOverLimit_ReturnsTwoChunks()
    {
        // 11 chars with a space at position 5: "Hello world"
        var chunks = MessageChunker.Split("Hello world", 8).ToList();

        chunks.Count.ShouldBe(2);
        chunks[0].ShouldBe("Hello");
        chunks[1].ShouldBe("world");
    }

    [Test]
    public void Split_VeryLongText_ReturnsMultipleChunks()
    {
        // Build text with spaces every 10 chars: "aaaaaaaaaa aaaaaaaaaa aaaaaaaaaa ..."
        var text = string.Join(" ", Enumerable.Repeat(new string('a', 10), 10));
        // Total length = 10*10 + 9 spaces = 109 chars

        var chunks = MessageChunker.Split(text, 25).ToList();

        chunks.Count.ShouldBeGreaterThan(2);
        chunks.ShouldAllBe(c => c.Length <= 25);
    }

    // ── Split point intelligence ───────────────────────────────────────

    [Test]
    public void Split_PrefersWhitespaceBreak_DoesNotSplitWords()
    {
        // "aaaa bbbb cccc" = 14 chars; limit 10
        // Should break at the space between "bbbb" and "cccc"
        var chunks = MessageChunker.Split("aaaa bbbb cccc", 10).ToList();

        chunks.Count.ShouldBe(2);
        chunks[0].ShouldBe("aaaa bbbb");
        chunks[1].ShouldBe("cccc");
    }

    [Test]
    public void Split_BreaksAtNewline_WhenNewlineIsClosestWhitespace()
    {
        var text = "Line one\nLine two\nLine three";
        // limit 12: "Line one\nLin" is 12 chars; last whitespace is \n at pos 8
        var chunks = MessageChunker.Split(text, 12).ToList();

        chunks.Count.ShouldBeGreaterThanOrEqualTo(2);
        chunks[0].ShouldBe("Line one");
        // The newline is consumed; next chunk starts with "Line two"
        chunks[1].ShouldStartWith("Line two");
    }

    [Test]
    public void Split_BreaksAtParagraphBoundary_WhenPossible()
    {
        var text = "Paragraph one.\n\nParagraph two.";
        // limit 20: "Paragraph one.\n\nPar" = 20 chars
        // Scanning backward from pos 19, first whitespace is \n at pos 16
        var chunks = MessageChunker.Split(text, 20).ToList();

        chunks.Count.ShouldBe(2);
        // Should break at one of the newlines in the paragraph boundary
        chunks[0].ShouldStartWith("Paragraph one.");
    }

    [Test]
    public void Split_HardCutsAtLimit_WhenNoWhitespaceExists()
    {
        var text = new string('x', 30);

        var chunks = MessageChunker.Split(text, 10).ToList();

        chunks.Count.ShouldBe(3);
        chunks[0].ShouldBe(new string('x', 10));
        chunks[1].ShouldBe(new string('x', 10));
        chunks[2].ShouldBe(new string('x', 10));
    }

    // ── Edge cases ─────────────────────────────────────────────────────

    [Test]
    public void Split_EmptyString_ReturnsNoChunks()
    {
        var chunks = MessageChunker.Split("", 100).ToList();

        chunks.ShouldBeEmpty();
    }

    [Test]
    public void Split_NullString_ReturnsNoChunks()
    {
        var chunks = MessageChunker.Split(null!, 100).ToList();

        chunks.ShouldBeEmpty();
    }

    [Test]
    public void Split_ZeroMaxLength_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            MessageChunker.Split("test", 0).ToList());
    }

    [Test]
    public void Split_NegativeMaxLength_ThrowsArgumentOutOfRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            MessageChunker.Split("test", -5).ToList());
    }

    [Test]
    public void Split_LimitOfOne_HardCutsEveryCharacter()
    {
        var chunks = MessageChunker.Split("abc", 1).ToList();

        chunks.Count.ShouldBe(3);
        chunks[0].ShouldBe("a");
        chunks[1].ShouldBe("b");
        chunks[2].ShouldBe("c");
    }

    [Test]
    public void Split_LimitOfOne_WithSpaces_SkipsSpaces()
    {
        // With limit=1, scanning backward from position 0 for whitespace
        // finds nothing (breakIndex stays -1), so hard-cut at 1 char.
        // But space at offset is special: breakIndex==offset so condition
        // breakIndex > offset is false -> hard-cut includes the space.
        var chunks = MessageChunker.Split("a b", 1).ToList();

        // "a b" with limit 1: first hard-cut "a", then " " is offset=1,
        // scan backward finds space at index 1 but breakIndex(1) > offset(1) is false,
        // so hard-cut " ", then "b"
        chunks.Count.ShouldBe(3);
        chunks[0].ShouldBe("a");
        chunks[1].ShouldBe(" ");
        chunks[2].ShouldBe("b");
    }

    [Test]
    public void Split_SingleLongWord_HardCutsAtLimit()
    {
        var word = new string('z', 25);

        var chunks = MessageChunker.Split(word, 10).ToList();

        chunks.Count.ShouldBe(3);
        chunks[0].Length.ShouldBe(10);
        chunks[1].Length.ShouldBe(10);
        chunks[2].Length.ShouldBe(5);
    }

    [Test]
    public void Split_TextWithOnlyNewlines_SplitsAtNewlines()
    {
        var text = "\n\n\n\n\n";

        var chunks = MessageChunker.Split(text, 3).ToList();

        // 5 newlines, limit 3: first scan finds \n at index 2 (breakIndex=2 > offset=0),
        // yields "\n\n", offset becomes 3. Remaining = "\n\n" which is <= 3, yields "\n\n".
        chunks.Count.ShouldBe(2);
    }

    [Test]
    public void Split_UnicodeCharacters_CountedByCharLength()
    {
        // Each emoji is typically 2 chars (surrogate pair) in .NET's UTF-16
        // But some are single char. Use known multi-char emoji.
        var text = "Hello \ud83d\ude00 World"; // "Hello [grin] World" — \ud83d\ude00 is 2 chars

        var chunks = MessageChunker.Split(text, 8).ToList();

        // "Hello \ud83d\ude00" = 8 chars (H,e,l,l,o, ,\ud83d,\ude00) — but space at index 5
        // is the last whitespace within limit 8 scanning backward from index 7
        chunks.Count.ShouldBeGreaterThanOrEqualTo(2);
        chunks.ShouldAllBe(c => c.Length <= 8);
    }

    // ── Completeness guarantees ────────────────────────────────────────

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    [TestCase(2000)]
    public void Split_AllChunksConcatenated_ContainsOriginalContent(int maxLength)
    {
        var text = "The quick brown fox jumps over the lazy dog. " +
                   "Pack my box with five dozen liquor jugs. " +
                   "How vexingly quick daft zebras jump.";

        var chunks = MessageChunker.Split(text, maxLength).ToList();

        // The whitespace at break points is consumed (not in either chunk),
        // so we verify by joining with the consumed separator.
        // More precisely: every char of the original must appear in some chunk,
        // except for whitespace chars that served as split points.
        var reassembled = string.Join(" ", chunks);
        // All non-whitespace content must survive
        text.Replace(" ", "").ShouldBe(reassembled.Replace(" ", ""));
    }

    [TestCase(5)]
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    [TestCase(4096)]
    public void Split_NoChunkExceedsLimit(int maxLength)
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 500));

        var chunks = MessageChunker.Split(text, maxLength).ToList();

        chunks.ShouldAllBe(c => c.Length <= maxLength);
    }

    [Test]
    public void Split_NoChunkExceedsLimit_WithNoWhitespace()
    {
        var text = new string('a', 100);

        var chunks = MessageChunker.Split(text, 7).ToList();

        chunks.ShouldAllBe(c => c.Length <= 7);
        // 100 / 7 = 14 remainder 2, so 15 chunks
        chunks.Count.ShouldBe(15);
    }

    // ── Realistic channel limits ───────────────────────────────────────

    [Test]
    public void Split_TelegramLimit_HandlesLongMessage()
    {
        const int telegramLimit = 4096;
        var text = string.Join("\n\n", Enumerable.Repeat(
            "This is a paragraph of text that simulates a real assistant response. " +
            "It contains multiple sentences to be realistic.",
            50));

        var chunks = MessageChunker.Split(text, telegramLimit).ToList();

        chunks.ShouldAllBe(c => c.Length <= telegramLimit);
        chunks.Count.ShouldBeGreaterThan(1);
    }

    [Test]
    public void Split_DiscordLimit_HandlesLongMessage()
    {
        const int discordLimit = 2000;
        var text = string.Join(" ", Enumerable.Repeat("word", 800));

        var chunks = MessageChunker.Split(text, discordLimit).ToList();

        chunks.ShouldAllBe(c => c.Length <= discordLimit);
        chunks.Count.ShouldBeGreaterThan(1);
    }

    // ── Whitespace consumption behavior ────────────────────────────────

    [Test]
    public void Split_WhitespaceAtBreakPoint_IsConsumed()
    {
        // "abc def" limit 4 -> break at space index 3.
        // Chunk 1 = "abc" (offset 0..3 exclusive), offset advances to 4.
        // Chunk 2 = "def" (remaining).
        var chunks = MessageChunker.Split("abc def", 4).ToList();

        chunks.Count.ShouldBe(2);
        chunks[0].ShouldBe("abc");
        chunks[1].ShouldBe("def");
        // The space is not in either chunk
        chunks[0].ShouldNotEndWith(" ");
        chunks[1].ShouldNotStartWith(" ");
    }

    [Test]
    public void Split_MultipleSpaces_BreaksAtLastWhitespaceInRange()
    {
        // "aa  bb  cc" limit 6: window is "aa  bb", last space is index 5... no,
        // "aa  bb" = a,a, , ,b,b -> scanning from index 5 backward, index 3 is space.
        // Actually index 5 is 'b', index 4 is 'b', index 3 is ' ', so break at 3.
        // Wait: "aa  bb  cc" indices: 0=a,1=a,2= ,3= ,4=b,5=b,6= ,7= ,8=c,9=c
        // limit 6: window 0..5, scanning backward from 5: 5=b,4=b,3=' ' -> break at 3
        var chunks = MessageChunker.Split("aa  bb  cc", 6).ToList();

        chunks.Count.ShouldBeGreaterThanOrEqualTo(2);
        chunks.ShouldAllBe(c => c.Length <= 6);
    }
}