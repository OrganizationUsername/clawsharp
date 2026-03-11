namespace Clawsharp.Security;

/// <summary>
///     Tracks cumulative suspicion score across tool results within a single
///     agent loop iteration. When multiple tool results contain instruction-like
///     content, the score rises and may trigger warnings or blocks.
/// </summary>
public sealed class SuspicionTracker
{
    /// <summary>Score threshold for injecting a warning into conversation context.</summary>
    public const int WarnThreshold = 3;

    /// <summary>Score threshold for halting tool execution in the current iteration.</summary>
    public const int BlockThreshold = 6;

    private int _score;

    /// <summary>Current cumulative suspicion score.</summary>
    public int Score => _score;

    /// <summary>Whether the warn threshold has been exceeded.</summary>
    public bool IsWarning => _score >= WarnThreshold;

    /// <summary>Whether the block threshold has been exceeded.</summary>
    public bool IsBlocked => _score >= BlockThreshold;

    /// <summary>
    ///     Record a suspicion event. Each injection detection in a tool result
    ///     adds to the cumulative score.
    /// </summary>
    /// <param name="points">Points to add (1 for standard pattern, 2 for indirect pattern).</param>
    public void RecordSuspicion(int points = 1) => Interlocked.Add(ref _score, points);

    /// <summary>Reset the tracker for a new request cycle.</summary>
    public void Reset() => Interlocked.Exchange(ref _score, 0);
}
