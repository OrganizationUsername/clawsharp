using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Skills;

[UsedImplicitly]
internal sealed class SkillsRemoveCommand : AsyncCommand<SkillsRemoveCommand.Settings>
{
    [UsedImplicitly]
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = "";
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var skillsDir = Path.GetFullPath(SkillRegistry.SkillsDir);
        var skillDir = Path.GetFullPath(Path.Combine(skillsDir, settings.Name));
        if (!skillDir.StartsWith(skillsDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine("[red]Invalid skill name: must not contain path separators or '..'[/]");
            return Task.FromResult(1);
        }

        if (!Directory.Exists(skillDir))
        {
            AnsiConsole.MarkupLine($"[red]Skill not found:[/] {Markup.Escape(settings.Name)}");
            AnsiConsole.MarkupLine("Run [cyan]clawsharp skills list[/] to see installed skills.");
            return Task.FromResult(1);
        }

        if (!AnsiConsole.Confirm($"Remove skill '{settings.Name}'?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return Task.FromResult(0);
        }

        Directory.Delete(skillDir, recursive: true);
        AnsiConsole.MarkupLine($"[green]Removed[/] {Markup.Escape(settings.Name)}");
        return Task.FromResult(0);
    }
}