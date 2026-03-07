using System.ComponentModel;
using Clawsharp.Config;
using Clawsharp.Cron;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Cron;

/// <summary>Shows the command to run a cron job immediately.</summary>
[UsedImplicitly]
public sealed class CronRunCommand : AsyncCommand<CronRunCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("Job ID or prefix to match")]
        public string Id { get; set; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var store = CronStoreFactory.Create(config);
        await store.InitAsync(cancellationToken);
        var jobs = await store.LoadAllAsync(cancellationToken);

        var match = jobs.FirstOrDefault(j => j.Id.StartsWith(settings.Id, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] No cron job found matching ID prefix '{Markup.Escape(settings.Id)}'.");
            return 1;
        }

        var name = match.Name ?? match.Id[..Math.Min(8, match.Id.Length)];
        AnsiConsole.MarkupLine($"To run job '[bold]{Markup.Escape(name)}[/]' immediately, use:");
        AnsiConsole.MarkupLine($"  [yellow]clawsharp agent -m \"{Markup.Escape(match.Message)}\"[/]");
        return 0;
    }
}