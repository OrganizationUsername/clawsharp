namespace Clawsharp.Config.Features;

/// <summary>
/// Configuration for the interaction analytics subsystem.
/// </summary>
public sealed class AnalyticsConfig
{
    /// <summary>Whether interaction logging is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Whether to store concise interaction summaries in the memory provider for later AI analysis.</summary>
    public bool StoreInMemory { get; init; }

    /// <summary>
    /// Storage backend: "jsonl" (default), "sqlite", "postgres", or "mssql".
    /// When set to a database backend, interactions are persisted via EF Core.
    /// </summary>
    public string Backend { get; init; } = "jsonl";

    /// <summary>
    /// Connection string for postgres/mssql backends.
    /// When null, falls back to the memory section's connection string.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Directory for the SQLite analytics database file (analytics.db).
    /// Supports ~ expansion. Defaults to ~/.clawsharp.
    /// </summary>
    public string Dir { get; init; } = "~/.clawsharp";
}
