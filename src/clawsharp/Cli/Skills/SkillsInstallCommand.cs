using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Skills;

[UsedImplicitly]
internal sealed class SkillsInstallCommand : AsyncCommand<SkillsInstallCommand.Settings>
{
    [UsedImplicitly]
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!SkillRegistry.KnownSkills.ContainsKey(settings.Name))
        {
            AnsiConsole.MarkupLine($"[red]Unknown skill:[/] {Markup.Escape(settings.Name)}");
            AnsiConsole.MarkupLine("Run [cyan]clawsharp skills search[/] to see available skills.");
            return 1;
        }

        await SkillRegistry.InstallSkillAsync(settings.Name, cancellationToken);
        return 0;
    }
}