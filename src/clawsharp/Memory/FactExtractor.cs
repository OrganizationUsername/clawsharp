using System.Collections.Concurrent;
using System.Text;

namespace Clawsharp.Memory;

/// <summary>
///     Accumulates conversation turns per session and signals when enough context
///     has been gathered to trigger LLM-based durable fact extraction.
///     Facts are extracted periodically (every 5+ turns with 200+ chars) rather than
///     every turn to amortize LLM cost.
/// </summary>
public sealed class FactExtractor
{
    /// <summary>Minimum number of turns before extraction is eligible.</summary>
    private const int MinTurns = 5;

    /// <summary>Minimum total character count before extraction is eligible.</summary>
    private const int MinChars = 200;

    private readonly ConcurrentDictionary<string, TurnBuffer> _buffers = new();

    /// <summary>
    ///     Accumulate a user + assistant turn. Returns <c>true</c> if extraction should be triggered.
    /// </summary>
    public bool AccumulateTurn(string sessionId, string userMessage, string assistantReply)
    {
        var buffer = _buffers.GetOrAdd(sessionId, _ => new TurnBuffer());
        return buffer.AddAndCheckEligibility(userMessage, assistantReply, MinTurns, MinChars);
    }

    /// <summary>
    ///     Get and reset the accumulated text for extraction.
    ///     Returns <c>null</c> if no meaningful content is buffered.
    /// </summary>
    public string? DrainBuffer(string sessionId)
    {
        if (!_buffers.TryGetValue(sessionId, out var buffer)) return null;
        var text = buffer.Drain();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private sealed class TurnBuffer
    {
        private readonly StringBuilder _sb = new();

        private readonly object _lock = new();

        public int TurnCount { get; private set; }

        public int TotalChars => _sb.Length;

        public bool AddAndCheckEligibility(string user, string assistant, int minTurns, int minChars)
        {
            lock (_lock)
            {
                _sb.Append("User: ").AppendLine(user);
                _sb.Append("Assistant: ").AppendLine(assistant);
                TurnCount++;
                return TurnCount >= minTurns && TotalChars >= minChars;
            }
        }

        public string Drain()
        {
            lock (_lock)
            {
                var text = _sb.ToString();
                _sb.Clear();
                TurnCount = 0;
                return text;
            }
        }
    }
}