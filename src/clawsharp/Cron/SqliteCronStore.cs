using Microsoft.Data.Sqlite;

namespace Clawsharp.Cron;

// TODO: SqliteCronStore, PostgresCronStore, and MssqlCronStore share the same ICronStore CRUD
// shape (~130 lines each) but differ in SQL dialect (ON CONFLICT vs MERGE), connection types
// (SqliteConnection vs NpgsqlConnection vs SqlConnection), boolean representation (INTEGER vs
// BOOLEAN vs BIT), and parameter binding. A CronStoreBase<TConnection> could extract the
// SemaphoreSlim locking and MapRow logic, but the SQL strings and parameter binding are
// dialect-specific enough that the extraction would be fragile. Deferring until a fourth
// backend is added or ADO.NET abstractions are unified.
public sealed class SqliteCronStore : ICronStore
{
    private readonly string _connectionString;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteCronStore(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                              CREATE TABLE IF NOT EXISTS cron_jobs (
                                  id TEXT PRIMARY KEY,
                                  name TEXT,
                                  schedule_kind TEXT NOT NULL,
                                  schedule_expr TEXT NOT NULL,
                                  tz TEXT,
                                  channel TEXT NOT NULL,
                                  message TEXT NOT NULL,
                                  sender_id TEXT NOT NULL,
                                  enabled INTEGER NOT NULL,
                                  created_at TEXT NOT NULL,
                                  last_run_at TEXT,
                                  run_count INTEGER NOT NULL,
                                  source TEXT NOT NULL,
                                  model TEXT,
                                  provider TEXT
                              );
                              """;
            await cmd.ExecuteNonQueryAsync(ct);

            // Migrate existing tables that lack model/provider columns.
            await using var alter = conn.CreateCommand();
            alter.CommandText = """
                                ALTER TABLE cron_jobs ADD COLUMN model TEXT;
                                """;
            try
            {
                await alter.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException)
            {
                /* column already exists */
            }

            await using var alter2 = conn.CreateCommand();
            alter2.CommandText = """
                                 ALTER TABLE cron_jobs ADD COLUMN provider TEXT;
                                 """;
            try
            {
                await alter2.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException)
            {
                /* column already exists */
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<CronJob>> LoadAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id,name,schedule_kind,schedule_expr,tz,channel,message,sender_id,enabled,created_at,last_run_at,run_count,source,model,provider FROM cron_jobs";
            var jobs = new List<CronJob>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                jobs.Add(MapRow(reader));
            }

            return jobs;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(CronJob job, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO cron_jobs (id,name,schedule_kind,schedule_expr,tz,channel,message,sender_id,enabled,created_at,last_run_at,run_count,source,model,provider)
                              VALUES (@id,@name,@sk,@se,@tz,@ch,@msg,@sid,@en,@ca,@lra,@rc,@src,@model,@provider)
                              ON CONFLICT(id) DO UPDATE SET
                                  name=excluded.name, schedule_kind=excluded.schedule_kind,
                                  schedule_expr=excluded.schedule_expr, tz=excluded.tz,
                                  channel=excluded.channel, message=excluded.message,
                                  sender_id=excluded.sender_id, enabled=excluded.enabled,
                                  last_run_at=excluded.last_run_at, run_count=excluded.run_count,
                                  source=excluded.source, model=excluded.model, provider=excluded.provider;
                              """;
            BindParams(cmd, job);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM cron_jobs WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateRunStatsAsync(string id, string lastRunAt, int runCount, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE cron_jobs SET last_run_at=@lra, run_count=@rc WHERE id=@id";
            cmd.Parameters.AddWithValue("@lra", lastRunAt);
            cmd.Parameters.AddWithValue("@rc", runCount);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static CronJob MapRow(SqliteDataReader r)
    {
        return new CronJob
        {
            Id = r.GetString(0),
            Name = r.IsDBNull(1) ? null : r.GetString(1),
            ScheduleKind = CronScheduleKind.FromValue(r.GetString(2)),
            ScheduleExpr = r.GetString(3),
            Tz = r.IsDBNull(4) ? null : r.GetString(4),
            Channel = r.GetString(5),
            Message = r.GetString(6),
            SenderId = r.GetString(7),
            Enabled = r.GetInt32(8) != 0,
            CreatedAt = r.GetString(9),
            LastRunAt = r.IsDBNull(10) ? null : r.GetString(10),
            RunCount = r.GetInt32(11),
            Source = CronSource.FromValue(r.GetString(12)),
            Model = r.IsDBNull(13) ? null : r.GetString(13),
            Provider = r.IsDBNull(14) ? null : r.GetString(14)
        };
    }

    private static void BindParams(SqliteCommand cmd, CronJob job)
    {
        cmd.Parameters.AddWithValue("@id", job.Id);
        cmd.Parameters.AddWithValue("@name", (object?)job.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sk", job.ScheduleKind.Value);
        cmd.Parameters.AddWithValue("@se", job.ScheduleExpr);
        cmd.Parameters.AddWithValue("@tz", (object?)job.Tz ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ch", job.Channel);
        cmd.Parameters.AddWithValue("@msg", job.Message);
        cmd.Parameters.AddWithValue("@sid", job.SenderId);
        cmd.Parameters.AddWithValue("@en", job.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@ca", job.CreatedAt);
        cmd.Parameters.AddWithValue("@lra", (object?)job.LastRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rc", job.RunCount);
        cmd.Parameters.AddWithValue("@src", job.Source.Value);
        cmd.Parameters.AddWithValue("@model", (object?)job.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@provider", (object?)job.Provider ?? DBNull.Value);
    }
}