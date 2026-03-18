using Clawsharp.Config;
using Clawsharp.Cron;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Cron;

/// <summary>Lists all scheduled cron jobs.</summary>
[UsedImplicitly]
public sealed class CronListCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var store = CronStoreFactory.Create(config);
        await store.InitAsync(cancellationToken);
        var jobs = await store.LoadAllAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey](no cron jobs configured)[/]");
            return 0;
        }

        var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("ID")
                    .AddColumn("Name")
                    .AddColumn("Kind")
                    .AddColumn("Schedule")
                    .AddColumn("Channel")
                    .AddColumn("Enabled")
                    .AddColumn("Last Run")
                    .AddColumn(new TableColumn("Runs").RightAligned());

        foreach (var job in jobs)
        {
            var id = job.Id;
            if (job.Id.Length > 8)
            {
                id = job.Id[..8];
            }

            var enabled = job.Enabled ? "[green]yes[/]" : "[grey]no[/]";
            table.AddRow(
                id,
                Markup.Escape(Truncate(job.Name ?? "(unnamed)", 20)),
                Markup.Escape(job.ScheduleKind.Value),
                Markup.Escape(Truncate(job.ScheduleExpr, 24)),
                Markup.Escape(Truncate(job.Channel, 10)),
                enabled,
                Markup.Escape(Truncate(job.LastRunAt?.ToString("O") ?? "-", 22)),
                job.RunCount.ToString());
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{jobs.Count} job(s) total.[/]");
        return 0;
    }

    private static string Truncate(string value, int maxLen)
    {
        if (value.Length > maxLen)
        {
            return string.Concat(value.AsSpan(0, maxLen - 1), "~");
        }

        return value;
    }
}