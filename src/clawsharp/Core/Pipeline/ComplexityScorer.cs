using System.Runtime.CompilerServices;

namespace Clawsharp.Core.Pipeline;

/// <summary>
/// Scores the complexity of an inbound message to support intelligent model routing.
/// Returns a value from 0 (trivial) to 100 (very complex). Messages below a configurable
/// threshold are routed to a cheaper/faster model.
/// </summary>
internal static class ComplexityScorer
{
    // Token-count thresholds
    private const int HighTokenThreshold = 500;

    private const int MediumTokenThreshold = 200;

    private const int LowTokenThreshold = 50;

    // Score contributions
    private const int HighTokenScore = 25;

    private const int MediumTokenScore = 15;

    private const int LowTokenScore = 5;

    private const int FullCodeBlockScore = 20;

    private const int PartialCodeBlockScore = 10;

    private const int InlineCodeScore = 5;

    private const int CjkContentScore = 10;

    private const int HighToolCallScore = 25;

    private const int LowToolCallScore = 15;

    private const int DeepConversationScore = 10;

    private const int MediumConversationScore = 5;

    // Tool call thresholds
    private const int HighToolCallThreshold = 3;

    // Conversation depth thresholds
    private const int DeepConversationThreshold = 10;

    private const int MediumConversationThreshold = 5;

    private const int MaxScore = 100;

    /// <summary>Score message complexity from 0-100.</summary>
    /// <param name="message">The user's message text.</param>
    /// <param name="recentToolCallCount">Number of tool-role messages in the last few turns.</param>
    /// <param name="conversationDepth">Total number of messages in the session.</param>
    public static int Score(string message, int recentToolCallCount, int conversationDepth)
    {
        var score = 0;

        // Token estimate (rough: 1 token ~ 4 chars for English, ~1.5 for CJK).
        var estimatedTokens = EstimateTokens(message);
        if (estimatedTokens > HighTokenThreshold)
            score += HighTokenScore;
        else if (estimatedTokens > MediumTokenThreshold)
            score += MediumTokenScore;
        else if (estimatedTokens > LowTokenThreshold)
            score += LowTokenScore;

        // Code blocks (``` pairs indicate pasted code or requests about code).
        var codeBlockCount = CountOccurrences(message, "```");
        if (codeBlockCount >= 2)
            score += FullCodeBlockScore; // at least one complete fenced block
        else if (codeBlockCount == 1)
            score += PartialCodeBlockScore; // partial block — still code-related

        // Inline code (backtick outside of fenced blocks).
        if (message.Contains('`'))
            score += InlineCodeScore;

        // CJK content — tokenizes at a higher rate, signals non-trivial content.
        if (HasCjkContent(message))
            score += CjkContentScore;

        // Recent tool calls indicate an ongoing complex workflow.
        if (recentToolCallCount > HighToolCallThreshold)
            score += HighToolCallScore;
        else if (recentToolCallCount > 0)
            score += LowToolCallScore;

        // Conversation depth — deep sessions tend to be complex.
        if (conversationDepth > DeepConversationThreshold)
            score += DeepConversationScore;
        else if (conversationDepth > MediumConversationThreshold)
            score += MediumConversationScore;

        return Math.Min(score, MaxScore);
    }

    /// <summary>
    /// Rough token count estimate. English text averages ~4 chars per token;
    /// CJK characters average ~0.67 tokens each (one char often becomes 2-3 byte-pair tokens,
    /// but the useful "information per token" is higher).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateTokens(string text)
    {
        var cjkChars = 0;
        var totalChars = text.Length;

        foreach (var c in text)
        {
            if (IsCjk(c))
                cjkChars++;
        }

        var nonCjk = totalChars - cjkChars;
        // ~0.25 tokens per non-CJK char, ~0.67 tokens per CJK char.
        return (nonCjk / 4) + (cjkChars * 2 / 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasCjkContent(string text)
    {
        foreach (var c in text)
        {
            if (IsCjk(c))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCjk(char c)
    {
        // CJK Unified Ideographs, Hiragana, Katakana, Hangul Syllables.
        return c is (>= '\u4E00' and <= '\u9FFF')
            or (>= '\u3040' and <= '\u30FF')
            or (>= '\uAC00' and <= '\uD7AF');
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }

        return count;
    }
}