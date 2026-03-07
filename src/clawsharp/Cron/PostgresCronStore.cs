using Npgsql;

namespace Clawsharp.Cron;

public sealed class PostgresCronStore : ICronStore
{
    private readonly string _connectionString;

    public PostgresCronStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
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
                              enabled BOOLEAN NOT NULL,
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
                            ALTER TABLE cron_jobs ADD COLUMN IF NOT EXISTS model TEXT;
                            ALTER TABLE cron_jobs ADD COLUMN IF NOT EXISTS provider TEXT;
                            """;
        await alter.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CronJob>> LoadAllAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id,name,schedule_kind,schedule_expr,tz,channel,message,sender_id,enabled,created_at,last_run_at,run_count,source,model,provider FROM cron_jobs";
        var jobs = new List<CronJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            jobs.Add(new CronJob
            {
                Id = reader.GetString(0),
                Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                ScheduleKind = CronScheduleKind.FromValue(reader.GetString(2)),
                ScheduleExpr = reader.GetString(3),
                Tz = reader.IsDBNull(4) ? null : reader.GetString(4),
                Channel = reader.GetString(5),
                Message = reader.GetString(6),
                SenderId = reader.GetString(7),
                Enabled = reader.GetBoolean(8),
                CreatedAt = reader.GetString(9),
                LastRunAt = reader.IsDBNull(10) ? null : reader.GetString(10),
                RunCount = reader.GetInt32(11),
                Source = CronSource.FromValue(reader.GetString(12)),
                Model = reader.IsDBNull(13) ? null : reader.GetString(13),
                Provider = reader.IsDBNull(14) ? null : reader.GetString(14)
            });
        }

        return jobs;
    }

    public async Task UpsertAsync(CronJob job, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO cron_jobs (id,name,schedule_kind,schedule_expr,tz,channel,message,sender_id,enabled,created_at,last_run_at,run_count,source,model,provider)
                          VALUES (@id,@name,@sk,@se,@tz,@ch,@msg,@sid,@en,@ca,@lra,@rc,@src,@model,@provider)
                          ON CONFLICT(id) DO UPDATE SET
                              name=EXCLUDED.name, schedule_kind=EXCLUDED.schedule_kind,
                              schedule_expr=EXCLUDED.schedule_expr, tz=EXCLUDED.tz,
                              channel=EXCLUDED.channel, message=EXCLUDED.message,
                              sender_id=EXCLUDED.sender_id, enabled=EXCLUDED.enabled,
                              last_run_at=EXCLUDED.last_run_at, run_count=EXCLUDED.run_count,
                              source=EXCLUDED.source, model=EXCLUDED.model, provider=EXCLUDED.provider;
                          """;
        cmd.Parameters.AddWithValue("@id", job.Id);
        cmd.Parameters.AddWithValue("@name", (object?)job.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sk", job.ScheduleKind.Value);
        cmd.Parameters.AddWithValue("@se", job.ScheduleExpr);
        cmd.Parameters.AddWithValue("@tz", (object?)job.Tz ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ch", job.Channel);
        cmd.Parameters.AddWithValue("@msg", job.Message);
        cmd.Parameters.AddWithValue("@sid", job.SenderId);
        cmd.Parameters.AddWithValue("@en", job.Enabled);
        cmd.Parameters.AddWithValue("@ca", job.CreatedAt);
        cmd.Parameters.AddWithValue("@lra", (object?)job.LastRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rc", job.RunCount);
        cmd.Parameters.AddWithValue("@src", job.Source.Value);
        cmd.Parameters.AddWithValue("@model", (object?)job.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@provider", (object?)job.Provider ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cron_jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateRunStatsAsync(string id, string lastRunAt, int runCount, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cron_jobs SET last_run_at=@lra, run_count=@rc WHERE id=@id";
        cmd.Parameters.AddWithValue("@lra", lastRunAt);
        cmd.Parameters.AddWithValue("@rc", runCount);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}