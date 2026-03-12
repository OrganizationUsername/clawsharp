using Clawsharp.Cli.Cron;
using Clawsharp.Tools.Ops;

namespace Clawsharp.Cron;

/// <summary>
/// Shared schedule parsing for cron expressions, "every:" durations, and "at:" one-shot datetimes.
/// Used by both <see cref="CronTool"/> and <see cref="CronAddCommand"/>.
/// </summary>
public static class CronScheduleParser
{
    public static bool TryParseSchedule(string input, out CronScheduleKind kind, out string expr, out string error)
    {
        input = input.Trim();
        error = "";

        if (input.StartsWith("every:", StringComparison.OrdinalIgnoreCase))
        {
            kind = CronScheduleKind.Every;
            var durationStr = input[6..].Trim();
            if (!TryParseDuration(durationStr, out var ms))
            {
                expr = "";
                error = $"Cannot parse duration '{durationStr}'. Use e.g. '5m', '30s', '2h', '500ms'.";
                return false;
            }

            expr = ms.ToString();
            return true;
        }

        if (input.StartsWith("at:", StringComparison.OrdinalIgnoreCase))
        {
            kind = CronScheduleKind.At;
            var dtStr = input[3..].Trim();
            if (!DateTimeOffset.TryParse(dtStr, out _))
            {
                expr = "";
                error = $"Cannot parse datetime '{dtStr}'. Use ISO 8601, e.g. '2026-03-15T09:00:00Z'.";
                return false;
            }

            expr = dtStr;
            return true;
        }

        // Assume 5-field cron expression
        kind = CronScheduleKind.Cron;
        var fields = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            expr = "";
            error = $"Expected 5-field cron expression (got {fields.Length}). Example: '0 9 * * 1-5'.";
            return false;
        }

        expr = input;
        return true;
    }

    public static bool TryParseDuration(string s, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (s.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(s[..^2], out var ms))
            {
                return false;
            }

            milliseconds = ms;
            return milliseconds > 0;
        }

        if (s.EndsWith('h') || s.EndsWith('H'))
        {
            if (!long.TryParse(s[..^1], out var h))
            {
                return false;
            }

            milliseconds = h * 3_600_000L;
            return milliseconds > 0;
        }

        if (s.EndsWith('m') || s.EndsWith('M'))
        {
            if (!long.TryParse(s[..^1], out var m))
            {
                return false;
            }

            milliseconds = m * 60_000L;
            return milliseconds > 0;
        }

        if (s.EndsWith('s') || s.EndsWith('S'))
        {
            if (!long.TryParse(s[..^1], out var sec))
            {
                return false;
            }

            milliseconds = sec * 1_000L;
            return milliseconds > 0;
        }

        // No suffix: treat as seconds
        if (!long.TryParse(s, out var plain))
        {
            return false;
        }

        milliseconds = plain * 1_000L;
        return milliseconds > 0;
    }
}