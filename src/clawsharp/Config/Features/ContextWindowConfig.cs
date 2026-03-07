using System.ComponentModel.DataAnnotations;

namespace Clawsharp.Config.Features;

/// <summary>Context window guard configuration.</summary>
public sealed class ContextWindowConfig
{
    /// <summary>Whether context window tracking and automatic compaction are enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    ///     Override the context window size (in tokens) instead of using the model lookup table.
    ///     Null means use the built-in model lookup.
    /// </summary>
    public int? ContextWindowTokens { get; init; }

    /// <summary>
    ///     Fraction of the context window that triggers compaction (0.0 - 1.0).
    ///     Default: 0.75 (compaction starts at 75% utilization).
    /// </summary>
    [Range(0.01, 0.99)]
    public double CompactionTriggerPercent { get; init; } = 0.75;

    /// <summary>
    ///     Fraction of the context window that triggers emergency force-compression (0.0 - 1.0).
    ///     Default: 0.90 (force-compress at 90% utilization).
    /// </summary>
    [Range(0.01, 0.99)]
    public double EmergencyTrimPercent { get; init; } = 0.90;
}