using System.ComponentModel.DataAnnotations;

namespace Clawsharp.Config.Features;

/// <summary>Configuration for automatic session history pruning.</summary>
public sealed class SessionPruningConfig
{
    /// <summary>Maximum number of messages to retain. Oldest non-system messages are dropped first.</summary>
    [Range(1, int.MaxValue)]
    public int? MaxMessages { get; init; }

    /// <summary>Drop non-system messages older than this many days.</summary>
    [Range(1, int.MaxValue)]
    public int? MaxAgeDays { get; init; }
}