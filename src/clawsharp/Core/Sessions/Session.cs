namespace Clawsharp.Core.Sessions;

public sealed class Session
{
    private string _id = "";

    public required string Id
    {
        get => _id;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Session Id must not be null or empty.", nameof(value));
            }

            _id = value;
        }
    }

    public List<ChatMessage> Messages { get; init; } = [];

    public int TotalMessageCount { get; set; }

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    /// <summary>When true, reasoning/thinking blocks are shown in replies.</summary>
    public bool ShowThinking { get; set; }

    /// <summary>
    ///     Removes stale or excess non-system messages from the session history.
    ///     System messages are never pruned. Messages without a <see cref="ChatMessage.Timestamp" />
    ///     (legacy) are treated as infinitely old when age pruning is applied.
    /// </summary>
    /// <param name="maxMessages">If set, keep at most this many messages (system messages excluded from the count).</param>
    /// <param name="maxAgeDays">If set, drop non-system messages older than this many days.</param>
    /// <returns><c>true</c> if any messages were removed; otherwise <c>false</c>.</returns>
    public bool Prune(int? maxMessages, int? maxAgeDays)
    {
        if (maxMessages is null && maxAgeDays is null)
        {
            return false;
        }

        var removed = false;

        // --- Age-based pruning ---
        if (maxAgeDays is { } days)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(days);
            removed = Messages.RemoveAll(m =>
                m.Role != MessageRole.System &&
                (m.Timestamp is null || m.Timestamp.Value < cutoff)) > 0;
        }

        // --- Count-based pruning ---
        if (maxMessages is { } max)
        {
            // Count non-system messages.
            var nonSystemCount = 0;
            foreach (var m in Messages)
            {
                if (m.Role != MessageRole.System)
                {
                    nonSystemCount++;
                }
            }

            if (nonSystemCount > max)
            {
                var toRemove = nonSystemCount - max;
                // Walk forward, removing the oldest non-system messages first.
                for (var i = 0; i < Messages.Count && toRemove > 0; /* no increment */)
                {
                    if (Messages[i].Role != MessageRole.System)
                    {
                        Messages.RemoveAt(i);
                        toRemove--;
                        removed = true;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        return removed;
    }
}