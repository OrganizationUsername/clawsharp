namespace Clawsharp.Cron;

/// <summary>Persistence interface for cron job storage.</summary>
public interface ICronStore
{
    /// <summary>Initialize the store (create tables/files if needed).</summary>
    Task InitAsync(CancellationToken ct = default);

    /// <summary>Load all cron jobs from the store.</summary>
    Task<IReadOnlyList<CronJob>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>Insert or update a cron job.</summary>
    Task UpsertAsync(CronJob job, CancellationToken ct = default);

    /// <summary>Delete a cron job by its ID.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Update the last run timestamp and run count for a job.</summary>
    Task UpdateRunStatsAsync(string id, string lastRunAt, int runCount, CancellationToken ct = default);
}