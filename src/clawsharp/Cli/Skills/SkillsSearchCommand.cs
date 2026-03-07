using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Skills;

[UsedImplicitly]
internal sealed class SkillsSearchCommand : AsyncCommand<SkillsSearchCommand.Settings>
{
    [UsedImplicitly]
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[query]")]
        public string? Query { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var query = settings.Query ?? "";

        var matches = SkillRegistry.Catalogue
                                   .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                               || s.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                                               || s.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
                                   .ToList();

        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No skills found matching \"{Markup.Escape(query)}\".[/]");
            return Task.FromResult(0);
        }

        var table = new Table()
                    .AddColumn("Name")
                    .AddColumn("Group")
                    .AddColumn("Risk")
                    .AddColumn("Description");

        foreach (var skill in matches)
        {
            table.AddRow(
                Markup.Escape(skill.Name),
                Markup.Escape(skill.Group),
                Markup.Escape(skill.Risk),
                Markup.Escape(skill.Description));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{matches.Count} result(s). Install with: clawsharp skills install <name>[/]");
        return Task.FromResult(0);
    }
}