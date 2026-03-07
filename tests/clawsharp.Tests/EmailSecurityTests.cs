namespace Clawsharp.Tests;

/// <summary>
/// Tests for Email channel security logic: quoted reply stripping, command prefix,
/// and sender allowlist.
/// Uses pattern replication approach — the relevant logic is replicated as local
/// static methods from EmailChannel.
/// </summary>
public sealed class EmailSecurityTests
{
    // Replicates EmailChannel.PollImapAsync quoted reply stripping logic
    private static string StripQuotedReplies(string rawBody)
    {
        var bodyLines = rawBody.Split('\n')
                               .Where(l => !l.TrimStart().StartsWith('>'))
                               .ToArray();
        return string.Join('\n', bodyLines).Trim();
    }

    // Replicates EmailChannel allowlist initialization logic
    private static (bool allowAll, HashSet<string>? allowed) BuildAllowlist(List<string>? allowFrom)
    {
        if (allowFrom is null)
        {
            return (true, null);
        }

        if (allowFrom.Count == 0)
        {
            return (false, null);
        }

        if (allowFrom.Contains("*"))
        {
            return (true, null);
        }

        return (false, new HashSet<string>(allowFrom, StringComparer.OrdinalIgnoreCase));
    }

    // Replicates EmailChannel sender allowlist check
    private static bool IsSenderAllowed(bool allowAll, HashSet<string>? allowed, string fromAddr)
    {
        return allowAll || (allowed?.Contains(fromAddr) ?? false);
    }

    // Replicates EmailChannel command prefix check
    private static bool PassesCommandPrefixCheck(string? commandPrefix, string subject, string body)
    {
        if (commandPrefix is null)
        {
            return true;
        }

        return subject.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase) ||
               body.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase);
    }

    // ── StripQuotedReplies tests ────────────────────────────────────

    [Test]
    public void StripQuotedReplies_QuotedLines_RemovesQuotedContent()
    {
        var input = "Hello\n> Previous message\nWorld";

        var result = StripQuotedReplies(input);

        result.ShouldContain("Hello");
        result.ShouldContain("World");
        result.ShouldNotContain("Previous message");
    }

    [Test]
    public void StripQuotedReplies_NoQuotedLines_PreservesAllContent()
    {
        var input = "Line 1\nLine 2\nLine 3";

        var result = StripQuotedReplies(input);

        result.ShouldBe("Line 1\nLine 2\nLine 3");
    }

    [Test]
    public void StripQuotedReplies_AllLinesQuoted_ReturnsEmpty()
    {
        var input = "> line 1\n> line 2\n> line 3";

        var result = StripQuotedReplies(input);

        result.ShouldBeEmpty();
    }

    [Test]
    public void StripQuotedReplies_IndentedQuotedLine_RemovesQuotedContent()
    {
        // Lines where TrimStart() reveals a '>' prefix are removed
        var input = "Hello\n  > indented quote\nWorld";

        var result = StripQuotedReplies(input);

        result.ShouldContain("Hello");
        result.ShouldContain("World");
        result.ShouldNotContain("indented quote");
    }

    // ── CommandPrefix tests ─────────────────────────────────────────

    [Test]
    public void PassesCommandPrefixCheck_NullPrefix_AllowsAllMessages()
    {
        PassesCommandPrefixCheck(null, "random subject", "random body").ShouldBeTrue();
    }

    [Test]
    public void PassesCommandPrefixCheck_SubjectMatch_ReturnsTrue()
    {
        PassesCommandPrefixCheck("!ask", "!ask What is the meaning of life?", "body text")
            .ShouldBeTrue();
    }

    [Test]
    public void PassesCommandPrefixCheck_BodyMatch_ReturnsTrue()
    {
        PassesCommandPrefixCheck("!ask", "unrelated subject", "!ask How are you?")
            .ShouldBeTrue();
    }

    [Test]
    public void PassesCommandPrefixCheck_CaseInsensitive_ReturnsTrue()
    {
        PassesCommandPrefixCheck("!ask", "!ASK something", "body").ShouldBeTrue();
        PassesCommandPrefixCheck("!ASK", "!ask something", "body").ShouldBeTrue();
    }

    [Test]
    public void PassesCommandPrefixCheck_NoMatch_ReturnsFalse()
    {
        PassesCommandPrefixCheck("!ask", "newsletter update", "Some newsletter content")
            .ShouldBeFalse();
    }

    [Test]
    public void PassesCommandPrefixCheck_PrefixNotAtStart_ReturnsFalse()
    {
        // Prefix must be at the START of subject or body
        PassesCommandPrefixCheck("!ask", "Re: something !ask", "body after some text !ask question")
            .ShouldBeFalse();
    }

    // ── AllowFrom tests ─────────────────────────────────────────────

    [Test]
    public void IsSenderAllowed_SpecificList_MatchesByAddress()
    {
        var (allowAll, allowed) = BuildAllowlist(["alice@example.com", "bob@example.com"]);

        allowAll.ShouldBeFalse();
        IsSenderAllowed(allowAll, allowed, "alice@example.com").ShouldBeTrue();
        IsSenderAllowed(allowAll, allowed, "bob@example.com").ShouldBeTrue();
        IsSenderAllowed(allowAll, allowed, "eve@example.com").ShouldBeFalse();

        // Case-insensitive (Email addresses)
        IsSenderAllowed(allowAll, allowed, "Alice@Example.COM").ShouldBeTrue();
    }
}