namespace Clawsharp.Memory;

/// <summary>
///     Extracts significant keywords from text for secondary memory recall.
///     Used by enhanced recall to run additional memory searches beyond the primary query.
/// </summary>
internal static class KeywordExpander
{
    /// <summary>
    ///     Extract significant keywords from text for secondary memory recall.
    ///     Only processes messages &gt;= 30 chars. Returns keywords &gt;= 4 chars,
    ///     excluding common English stop words.
    /// </summary>
    public static IReadOnlyList<string> ExtractKeywords(string text)
    {
        if (text.Length < 30) return [];

        var words = text.Split(
            [
                ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '|', '@', '#', '$',
                '%', '&', '*', '+', '=', '<', '>', '~', '`'
            ],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words)
        {
            if (word.Length < 4) continue;
            if (IsStopWord(word)) continue;
            keywords.Add(word.ToLowerInvariant());
        }

        return [.. keywords];
    }

    private static bool IsStopWord(ReadOnlySpan<char> word) =>
        word.Equals("this", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("that", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("with", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("from", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("have", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("been", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("were", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("will", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("would", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("could", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("should", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("what", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("when", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("where", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("which", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("about", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("some", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("your", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("their", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("them", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("they", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("than", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("then", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("also", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("just", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("like", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("more", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("very", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("only", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("each", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("does", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("into", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("here", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("there", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("these", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("those", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("other", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("most", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("same", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("such", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("well", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("want", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("know", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("make", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("think", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("really", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("because", StringComparison.OrdinalIgnoreCase);
}