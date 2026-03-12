using System.Text;
using System.Text.Json;
using Clawsharp.Core.Services;
using Clawsharp.Cron;

namespace Clawsharp.Tools.Ops;

public sealed class CronTool : Tool
{
    private readonly CronService _cronService;

    public CronTool(CronService cronService)
    {
        _cronService = cronService;
    }

    public string DefaultChannel => ToolRegistry.CurrentChannelName ?? "cli";

    public override string Name => "cron";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Critical;

    public override string Description =>
        "Manage scheduled tasks. Actions: add, list, remove, run (fire immediately), update. " +
        "Schedule formats: '0 9 * * 1-5' (cron, 5-field), 'every:5m' / 'every:2h' / 'every:30s' (interval), " +
        "'at:2026-03-15T09:00:00Z' (one-shot, ISO 8601).";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "action": {
                                                         "type": "string",
                                                         "enum": ["add", "list", "remove", "run", "update"],
                                                         "description": "The operation to perform"
                                                       },
                                                       "id": { "type": "string", "description": "Job ID (required for remove, run, update)" },
                                                       "name": { "type": "string", "description": "Optional human-readable job name" },
                                                       "schedule": {
                                                         "type": "string",
                                                         "description": "When to fire: cron '0 9 * * 1-5', interval 'every:5m', one-shot 'at:2026-03-15T09:00:00Z'"
                                                       },
                                                       "channel": { "type": "string", "description": "Target channel (cli, telegram, discord, …)" },
                                                       "message": { "type": "string", "description": "Prompt/message to inject on schedule" },
                                                       "tz": { "type": "string", "description": "IANA timezone (e.g. 'America/New_York', 'Europe/London'). Schedule evaluated in this timezone. Default: UTC." },
                                                       "enabled": { "type": "boolean", "description": "Enable or disable the job (default true)" },
                                                       "model": { "type": "string", "description": "Model override for this job (e.g. 'gpt-4o', 'claude-sonnet-4-20250514'). Uses default if omitted." },
                                                       "provider": { "type": "string", "description": "Provider override for this job (e.g. 'openai', 'anthropic'). Uses default if omitted." }
                                                     },
                                                     "required": ["action"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var action = args.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        return action.ToLowerInvariant() switch
        {
            "add" => await AddAsync(args, ct),
            "list" => await ListAsync(ct),
            "remove" => await RemoveAsync(args, ct),
            "run" => await RunAsync(args, ct),
            "update" => await UpdateAsync(args, ct),
            _ => $"Unknown action '{action}'. Valid: add, list, remove, run, update."
        };
    }

    /// <summary>
    ///     Returns an error if the current async flow is inside a cron job execution,
    ///     preventing infinite self-scheduling loops.
    /// </summary>
    private static string? CheckCronSelfScheduling()
    {
        return CronContext.IsInCronExecution
            ? "Cannot schedule cron jobs from within a cron job execution."
            : null;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task<string> AddAsync(JsonElement args, CancellationToken ct)
    {
        var selfScheduleError = CheckCronSelfScheduling();
        if (selfScheduleError is not null)
        {
            return selfScheduleError;
        }

        var scheduleStr = args.TryGetProperty("schedule", out var se) ? se.GetString() : null;
        var message = args.TryGetProperty("message", out var me) ? me.GetString() : null;

        if (string.IsNullOrWhiteSpace(scheduleStr))
        {
            return "Error: 'schedule' is required for add.";
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return "Error: 'message' is required for add.";
        }

        if (!TryParseSchedule(scheduleStr, out var kind, out var expr, out var parseError))
        {
            return $"Error: invalid schedule — {parseError}";
        }

        var tz = args.TryGetProperty("tz", out var tzEl) ? tzEl.GetString() : null;
        if (!string.IsNullOrEmpty(tz))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(tz);
            }
            catch (TimeZoneNotFoundException)
            {
                return $"Unknown timezone '{tz}'. Use IANA format (e.g. 'America/New_York') or Windows format.";
            }
        }

        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = args.TryGetProperty("name", out var na) ? na.GetString() : null,
            ScheduleKind = kind,
            ScheduleExpr = expr,
            Tz = tz,
            Channel = (args.TryGetProperty("channel", out var ch) ? ch.GetString() : null) ?? DefaultChannel,
            Message = message,
            Enabled = !args.TryGetProperty("enabled", out var en) || en.GetBoolean(),
            Model = args.TryGetProperty("model", out var mo) ? mo.GetString() : null,
            Provider = args.TryGetProperty("provider", out var pr) ? pr.GetString() : null
        };

        await _cronService.AddJobAsync(job, ct);
        var preview = message.Length > 60 ? message[..60] + "…" : message;
        return $"Added job '{job.Id[..8]}': [{kind.Value}:{expr}] → {job.Channel} — {preview}";
    }

    private async Task<string> ListAsync(CancellationToken ct)
    {
        var jobs = await _cronService.ListJobsAsync(ct);
        if (jobs.Count == 0)
        {
            return "No cron jobs scheduled.";
        }

        var sb = new StringBuilder($"Cron jobs ({jobs.Count}):\n");
        foreach (var job in jobs)
        {
            var status = job.Enabled ? "enabled " : "disabled";
            var lastRun = job.LastRunAt is not null
                ? $"last={job.LastRunAt[..Math.Min(19, job.LastRunAt.Length)]}"
                : "never run";
            var preview = job.Message.Length > 50 ? job.Message[..50] + "…" : job.Message;
            var shortId = job.Id.Length > 8 ? job.Id[..8] : job.Id;
            var tzLabel = !string.IsNullOrEmpty(job.Tz) ? $" tz={job.Tz}" : "";
            var overrideLabel = "";
            if (job.Provider is not null || job.Model is not null)
            {
                var parts = new List<string>(2);
                if (job.Provider is not null)
                {
                    parts.Add($"provider={job.Provider}");
                }

                if (job.Model is not null)
                {
                    parts.Add($"model={job.Model}");
                }

                overrideLabel = $" ({string.Join(", ", parts)})";
            }

            sb.AppendLine(
                $"  [{shortId}] {status} [{job.ScheduleKind.Value}:{job.ScheduleExpr}]{tzLabel} → {job.Channel}: {preview} ({lastRun}, runs={job.RunCount}){overrideLabel}");
            if (job.Name is not null)
            {
                sb.AppendLine($"    Name: {job.Name}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string> RemoveAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Error: 'id' is required for remove.";
        }

        var removed = await _cronService.RemoveJobAsync(id, ct);
        return removed ? $"Removed job '{id}'." : $"No job found with id '{id}'.";
    }

    private async Task<string> RunAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Error: 'id' is required for run.";
        }

        return await _cronService.RunJobNowAsync(id, ct);
    }

    private async Task<string> UpdateAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Error: 'id' is required for update.";
        }

        var all = await _cronService.ListJobsAsync(ct);
        var job = all.FirstOrDefault(j =>
            j.Id == id || j.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            return $"No job found with id '{id}'.";
        }

        // Validate tz if provided for update
        string? updatedTz = job.Tz;
        if (args.TryGetProperty("tz", out var tzEl))
        {
            var tzStr = tzEl.GetString();
            if (!string.IsNullOrEmpty(tzStr))
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(tzStr);
                }
                catch (TimeZoneNotFoundException)
                {
                    return $"Unknown timezone '{tzStr}'. Use IANA format (e.g. 'America/New_York') or Windows format.";
                }
            }

            updatedTz = string.IsNullOrEmpty(tzStr) ? null : tzStr;
        }

        // Parse schedule upfront so we can set init-only properties during construction.
        var newScheduleKind = job.ScheduleKind;
        var newScheduleExpr = job.ScheduleExpr;
        if (args.TryGetProperty("schedule", out var sch) && sch.GetString() is { } scheduleStr
                                                         && !string.IsNullOrWhiteSpace(scheduleStr))
        {
            if (!TryParseSchedule(scheduleStr, out var kind, out var expr, out var parseError))
            {
                return $"Error: invalid schedule — {parseError}";
            }

            newScheduleKind = kind;
            newScheduleExpr = expr;
        }

        var updated = new CronJob
        {
            Id = job.Id,
            Name = args.TryGetProperty("name", out var na) ? na.GetString() : job.Name,
            ScheduleKind = newScheduleKind,
            ScheduleExpr = newScheduleExpr,
            Tz = updatedTz,
            Channel = (args.TryGetProperty("channel", out var ch) ? ch.GetString() : null) ?? job.Channel,
            Message = (args.TryGetProperty("message", out var me) ? me.GetString() : null) ?? job.Message,
            Enabled = args.TryGetProperty("enabled", out var en) ? en.GetBoolean() : job.Enabled,
            SenderId = job.SenderId,
            CreatedAt = job.CreatedAt,
            LastRunAt = job.LastRunAt,
            RunCount = job.RunCount,
            Source = job.Source,
            Model = args.TryGetProperty("model", out var mo) ? mo.GetString() : job.Model,
            Provider = args.TryGetProperty("provider", out var pr) ? pr.GetString() : job.Provider
        };

        var result = await _cronService.UpdateJobAsync(updated, ct);
        return result is null ? $"No job found with id '{id}'." : $"Updated job '{result.Id}'.";
    }

    // Schedule parsing delegated to shared CronScheduleParser
    private static bool TryParseSchedule(string input, out CronScheduleKind kind, out string expr, out string error)
        => CronScheduleParser.TryParseSchedule(input, out kind, out expr, out error);
}