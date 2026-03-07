using System.ComponentModel.DataAnnotations;

namespace Clawsharp.Config.Memory;

/// <summary>Configuration for memory fact decay and automatic pruning.</summary>
public sealed class MemoryDecayConfig
{
    /// <summary>Whether memory decay and automatic pruning is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Maximum age in days before a fact is eligible for hard deletion.</summary>
    [Range(1, 36500)]
    public int TtlDays { get; init; } = 30;

    /// <summary>Half-life in days for soft decay scoring (score halves every N days).</summary>
    [Range(1, 36500)]
    public double HalfLifeDays { get; init; } = 7.0;

    /// <summary>Hours between automatic prune sweeps.</summary>
    [Range(1, 8760)]
    public int PruneIntervalHours { get; init; } = 12;
}