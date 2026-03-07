using System.Runtime.CompilerServices;
using System.Text;

namespace Clawsharp.Providers;

/// <summary>
/// Parses a Server-Sent Events stream, yielding (event, data) pairs.
/// Handles multi-line data values, blank-line delimiters, comment lines (starting with ':'),
/// and end-of-stream flushing. The <c>[DONE]</c> sentinel is passed through as data;
/// callers should check for it if their protocol uses it.
/// <para>
/// Implements the SSE parsing algorithm per
/// <see href="https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation"/>.
/// </para>
/// </summary>
internal static class SseLineReader
{
    /// <summary>
    /// Reads an SSE stream and yields <c>(Event, Data)</c> tuples as they become available.
    /// <list type="bullet">
    /// <item><c>Event</c> is the most recent <c>event:</c> field value, or <c>null</c> if none was set.</item>
    /// <item><c>Data</c> is the accumulated data from one or more <c>data:</c> lines (joined with <c>\n</c>).</item>
    /// </list>
    /// A tuple is emitted on each blank line (the SSE event delimiter) and, if pending data exists,
    /// when the stream ends.
    /// </summary>
    public static async IAsyncEnumerable<(string? Event, string Data)> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true);

        string? currentEvent = null;
        StringBuilder? dataBuilder = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                // End of stream: flush any pending data.
                if (dataBuilder is { Length: > 0 })
                {
                    yield return (currentEvent, dataBuilder.ToString());
                }

                yield break;
            }

            // Blank line: dispatch accumulated event+data, then reset.
            if (line.Length == 0)
            {
                if (dataBuilder is { Length: > 0 })
                {
                    yield return (currentEvent, dataBuilder.ToString());
                    dataBuilder.Clear();
                }

                currentEvent = null;
                continue;
            }

            // Comment lines (starting with ':') are ignored per SSE spec.
            if (line[0] == ':')
            {
                continue;
            }

            // Parse "field: value" or "field:value" (space after colon is optional but conventional).
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0)
            {
                // Line with no colon: field name is the entire line, value is empty string.
                // Per SSE spec, only "data", "event", "id", "retry" are meaningful.
                continue;
            }

            var field = line.AsSpan(0, colonIdx);
            // Value starts after the colon; skip one leading space if present (per SSE spec).
            var valueStart = colonIdx + 1;
            if (valueStart < line.Length && line[valueStart] == ' ')
            {
                valueStart++;
            }

            var value = line.AsSpan(valueStart);

            if (field.SequenceEqual("event".AsSpan()))
            {
                currentEvent = value.ToString();
            }
            else if (field.SequenceEqual("data".AsSpan()))
            {
                dataBuilder ??= new StringBuilder(256);
                if (dataBuilder.Length > 0)
                {
                    dataBuilder.Append('\n');
                }

                dataBuilder.Append(value);
            }
            // "id" and "retry" fields are ignored — not relevant for LLM provider streams.
        }
    }
}