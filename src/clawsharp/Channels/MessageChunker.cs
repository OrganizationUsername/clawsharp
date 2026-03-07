namespace Clawsharp.Channels;

/// <summary>
/// Splits text into chunks at most <paramref name="maxLength"/> characters,
/// breaking on whitespace when possible so words are not split across chunks.
/// </summary>
public static class MessageChunker
{
    /// <summary>Splits <paramref name="text"/> into chunks of at most <paramref name="maxLength"/> characters.</summary>
    /// <param name="text">The text to split.</param>
    /// <param name="maxLength">Maximum characters per chunk (must be &gt; 0).</param>
    /// <returns>An enumerable of non-empty text chunks.</returns>
    public static IEnumerable<string> Split(string text, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxLength, 0);

        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        if (text.Length <= maxLength)
        {
            yield return text;
            yield break;
        }

        var offset = 0;
        while (offset < text.Length)
        {
            var remaining = text.Length - offset;
            if (remaining <= maxLength)
            {
                yield return text[offset..];
                yield break;
            }

            // Try to find a whitespace break point within the limit.
            var breakIndex = -1;
            for (var i = offset + maxLength - 1; i >= offset; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    breakIndex = i;
                    break;
                }
            }

            if (breakIndex > offset)
            {
                yield return text[offset..breakIndex];
                // Skip the whitespace character itself.
                offset = breakIndex + 1;
            }
            else
            {
                // No whitespace found -- hard-cut at maxLength.
                yield return text[offset..(offset + maxLength)];
                offset += maxLength;
            }
        }
    }
}