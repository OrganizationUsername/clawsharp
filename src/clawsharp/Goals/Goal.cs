using System.Collections.Frozen;

namespace Clawsharp.Goals;

public enum GoalStatus
{
    Active,

    Paused,

    Completed,

    Deleted
}

public sealed class GoalStep
{
    public string Text { get; init; } = "";

    public bool Done { get; set; }
}

public sealed class Goal
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    public string Title { get; init; } = "";

    public string? Description { get; set; }

    public GoalStatus Status { get; set; } = GoalStatus.Active;

    public List<GoalStep> Steps { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Valid state transitions for goal status. Completed and Deleted are terminal states.
    /// Active -> Paused, Completed, Deleted
    /// Paused -> Active (resume), Deleted
    /// </summary>
    private static readonly FrozenDictionary<GoalStatus, GoalStatus[]> ValidTransitions =
        new Dictionary<GoalStatus, GoalStatus[]>
        {
            [GoalStatus.Active] = [GoalStatus.Paused, GoalStatus.Completed, GoalStatus.Deleted],
            [GoalStatus.Paused] = [GoalStatus.Active, GoalStatus.Deleted],
            [GoalStatus.Completed] = [],
            [GoalStatus.Deleted] = [],
        }.ToFrozenDictionary();

    /// <summary>Returns true if transitioning from <paramref name="from"/> to <paramref name="to"/> is allowed.</summary>
    public static bool CanTransition(GoalStatus from, GoalStatus to)
        => ValidTransitions.TryGetValue(from, out var targets) && targets.AsSpan().Contains(to);

    /// <summary>Returns the set of valid target statuses from the given status, or an empty array for terminal states.</summary>
    public static GoalStatus[] GetValidTransitions(GoalStatus from)
        => ValidTransitions.TryGetValue(from, out var targets) ? targets : [];
}