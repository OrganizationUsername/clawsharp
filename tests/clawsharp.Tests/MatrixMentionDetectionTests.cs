namespace Clawsharp.Tests;

/// <summary>
/// Tests for Matrix channel mention detection logic: localpart extraction from MXID
/// and message body mention checking.
/// Uses pattern replication approach from MatrixChannel.SyncOnceAsync.
/// </summary>
public sealed class MatrixMentionDetectionTests
{
    // Replicates MatrixChannel localpart extraction from _selfId
    // Logic from SyncOnceAsync: var localpart = _selfId.Split(':')[0].TrimStart('@');
    private static string ExtractLocalpart(string mxid)
    {
        return mxid.Split(':')[0].TrimStart('@');
    }

    // Replicates MatrixChannel mention detection
    // Logic from SyncOnceAsync: text.Contains(localpart, StringComparison.OrdinalIgnoreCase)
    private static bool ContainsMention(string text, string localpart)
    {
        return text.Contains(localpart, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public void ExtractLocalpart_ValidMxid_ReturnsLocalpart()
    {
        ExtractLocalpart("@bot:matrix.org").ShouldBe("bot");
    }

    [Test]
    public void ExtractLocalpart_MissingAtPrefix_ReturnsLocalpart()
    {
        // If MXID doesn't start with '@', TrimStart('@') is a no-op
        ExtractLocalpart("bot:matrix.org").ShouldBe("bot");
    }

    [Test]
    public void ContainsMention_TextContainsLocalpart_ReturnsTrue()
    {
        ContainsMention("hello bot how are you", "bot").ShouldBeTrue();
    }

    [Test]
    public void ContainsMention_TextMissingLocalpart_ReturnsFalse()
    {
        ContainsMention("hello world", "bot").ShouldBeFalse();
    }

    [Test]
    public void ContainsMention_SubstringMatch_ReturnsTrue()
    {
        // "robotics" contains "bot" as a substring — this matches because
        // the production code uses string.Contains, not word-boundary matching
        ContainsMention("robotics is cool", "bot").ShouldBeTrue();
    }

    [Test]
    public void ContainsMention_CaseInsensitive_ReturnsTrue()
    {
        ContainsMention("Hello BOT", "bot").ShouldBeTrue();
        ContainsMention("hello bot", "BOT").ShouldBeTrue();
    }
}