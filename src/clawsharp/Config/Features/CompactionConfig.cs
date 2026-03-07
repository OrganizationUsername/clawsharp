namespace Clawsharp.Config.Features;

/// <summary>Configuration for conversation compaction (summarization of old messages).</summary>
public sealed class CompactionConfig
{
    /// <summary>Whether LLM-based compaction is enabled. If false, only emergency force-compression is used.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Number of recent messages to keep verbatim (not summarized).</summary>
    public int KeepRecent { get; init; } = 20;

    /// <summary>Maximum character length of the compaction summary.</summary>
    public int MaxSummaryChars { get; init; } = 2000;

    /// <summary>Maximum total characters of source messages fed to the summarizer.</summary>
    public int MaxSourceChars { get; init; } = 12000;

    /// <summary>Whether to flush key facts to long-term memory before compacting.</summary>
    public bool PreCompactionMemoryFlush { get; init; } = true;
}