using System.ComponentModel;
using Clawsharp.Config;
using Clawsharp.Cron;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Cron;

/// <summary>Adds a new scheduled cron job.</summary>
[UsedImplicitly]
public sealed class CronAddCommand : AsyncCommand<CronAddCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<schedule>")]
        [Description("Cron expression, 'every:5m', or 'at:2026-03-15T09:00Z'")]
        public string Schedule { get; set; } = "";

        [CommandArgument(1, "<message>")]
        [Description("Prompt text to send on schedule")]
        public string Message { get; set; } = "";

        [CommandOption("-n|--name")]
        [Description("Optional job name")]
        public string? Name { get; set; }

        [CommandOption("-c|--channel")]
        [Description("Target channel (default: cli)")]
        [DefaultValue("cli")]
        public string Channel { get; set; } = "cli";

        [CommandOption("-t|--tz")]
        [Description("IANA timezone for schedule evaluation (e.g. 'America/New_York')")]
        public string? Tz { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!CronScheduleParser.TryParseSchedule(settings.Schedule, out var kind, out var expr, out var parseError))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(parseError)}");
            return 1;
        }

        if (!string.IsNullOrEmpty(settings.Tz))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(settings.Tz);
            }
            catch (TimeZoneNotFoundException)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] Unknown timezone [bold]'{Markup.Escape(settings.Tz)}'[/]. Use IANA format (e.g. 'America/New_York') or Windows format.");
                return 1;
            }
        }

        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = settings.Name,
            ScheduleKind = kind,
            ScheduleExpr = expr,
            Tz = string.IsNullOrEmpty(settings.Tz) ? null : settings.Tz,
            Channel = settings.Channel,
            Message = settings.Message,
            SenderId = "cron",
            Enabled = true,
            Source = CronSource.Agent,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var config = ClawsharpConfiguration.GetAppConfig();
        var store = CronStoreFactory.Create(config);
        await store.InitAsync(cancellationToken);
        await store.UpsertAsync(job, cancellationToken);

        AnsiConsole.MarkupLine($"[green]Created[/] cron job [cyan]{job.Id[..8]}[/] " +
                               $"(kind=[bold]{Markup.Escape(kind.Value)}[/], expr=[bold]{Markup.Escape(expr)}[/], " +
                               $"channel=[bold]{Markup.Escape(settings.Channel)}[/])");
        return 0;
    }
}