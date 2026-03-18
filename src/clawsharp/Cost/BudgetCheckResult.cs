namespace Clawsharp.Cost;

/// <summary>Result of a budget enforcement check.</summary>
public enum BudgetStatus
{
    /// <summary>Request is within budget limits.</summary>
    Allowed,

    /// <summary>Request is approaching a budget limit.</summary>
    Warning,

    /// <summary>Request would exceed a budget limit.</summary>
    Exceeded
}

/// <summary>Outcome of checking budget before an LLM request.</summary>
public sealed record BudgetCheckResult(
    BudgetStatus Status,
    string? Message = null,
    decimal CurrentDailyUsd = 0,
    decimal CurrentMonthlyUsd = 0
);