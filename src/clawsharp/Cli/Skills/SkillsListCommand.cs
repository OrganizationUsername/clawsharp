using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Skills;

[UsedImplicitly]
internal sealed class SkillsListCommand : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var skillsDir = SkillRegistry.SkillsDir;

        if (!Directory.Exists(skillsDir))
        {
            AnsiConsole.MarkupLine("[grey]No skills installed yet.[/]");
            AnsiConsole.MarkupLine("Run [cyan]clawsharp skills install <name>[/] to install a skill.");
            return Task.FromResult(0);
        }

        var dirs = Directory.GetDirectories(skillsDir)
                            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal)
                            .ToList();

        if (dirs.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No skills installed yet.[/]");
            AnsiConsole.MarkupLine("Run [cyan]clawsharp skills install <name>[/] to install a skill.");
            return Task.FromResult(0);
        }

        var table = new Table()
                    .AddColumn("Name")
                    .AddColumn("Path");

        foreach (var dir in dirs)
        {
            table.AddRow(Markup.Escape(Path.GetFileName(dir)), Markup.Escape(dir));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{dirs.Count} skill(s) installed.[/]");
        return Task.FromResult(0);
    }
}