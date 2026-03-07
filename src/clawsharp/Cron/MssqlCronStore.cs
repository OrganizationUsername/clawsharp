using Microsoft.Data.SqlClient;

namespace Clawsharp.Cron;

public sealed class MssqlCronStore : ICronStore
{
    private readonly string _connectionString;

    public MssqlCronStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'cron_jobs')
                          CREATE TABLE cron_jobs (
                              id NVARCHAR(50) PRIMARY KEY,
                              name NVARCHAR(255),
                              schedule_kind NVARCHAR(10) NOT NULL,
                              schedule_expr NVARCHAR(255) NOT NULL,
                              tz NVARCHAR(50),
                              channel NVARCHAR(50) NOT NULL,
                              message NVARCHAR(MAX) NOT NULL,
                              sender_id NVARCHAR(100) NOT NULL,
                              enabled BIT NOT NULL,
                              created_at NVARCHAR(50) NOT NULL,
                              last_run_at NVARCHAR(50),
                              run_count INT NOT NULL,
                              source NVARCHAR(20) NOT NULL,
                              model NVARCHAR(255),
                              provider NVARCHAR(255)
                          );
                          """;
        await cmd.ExecuteNonQueryAsync(ct);

        // Migrate existing tables that lack model/provider columns.
        await using var alter = conn.CreateCommand();
        alter.CommandText = """
                            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('cron_jobs') AND name = 'model')
                                ALTER TABLE cron_jobs ADD model NVARCHAR(255);
                            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('cron_jobs') AND name = 'provider')
                                ALTER TABLE cron_jobs ADD provider NVARCHAR(255);
                            """;
        await alter.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CronJob>> LoadAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
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
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          MERGE cron_jobs AS target
                          USING (VALUES (@id,@name,@sk,@se,@tz,@ch,@msg,@sid,@en,@ca,@lra,@rc,@src,@model,@provider))
                              AS source (id,name,schedule_kind,schedule_expr,tz,channel,message,sender_id,enabled,created_at,last_run_at,run_count,source,model,provider)
                          ON target.id = source.id
                          WHEN MATCHED THEN UPDATE SET
                              name=source.name, schedule_kind=source.schedule_kind,
                              schedule_expr=source.schedule_expr, tz=source.tz,
                              channel=source.channel, message=source.message,
                              sender_id=source.sender_id, enabled=source.enabled,
                              last_run_at=source.last_run_at, run_count=source.run_count,
                              source=source.source, model=source.model, provider=source.provider
                          WHEN NOT MATCHED THEN INSERT (id,name,schedule_kind,schedule_expr,tz,channel,message,sender_id,enabled,created_at,last_run_at,run_count,source,model,provider)
                              VALUES (source.id,source.name,source.schedule_kind,source.schedule_expr,source.tz,source.channel,source.message,source.sender_id,source.enabled,source.created_at,source.last_run_at,source.run_count,source.source,source.model,source.provider);
                          """;
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
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cron_jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateRunStatsAsync(string id, string lastRunAt, int runCount, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cron_jobs SET last_run_at=@lra, run_count=@rc WHERE id=@id";
        cmd.Parameters.AddWithValue("@lra", lastRunAt);
        cmd.Parameters.AddWithValue("@rc", runCount);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}