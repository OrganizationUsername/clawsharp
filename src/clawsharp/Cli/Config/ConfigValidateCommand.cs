using Clawsharp.Config;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Config;

/// <summary>Validates the configuration file and exits with code 0 (valid) or 1 (issues).</summary>
[UsedImplicitly]
public sealed class ConfigValidateCommand : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        AppConfig config;
        try
        {
            config = ClawsharpConfiguration.GetAppConfig();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(ex.Message)}");
            return Task.FromResult(1);
        }

        var errors = ConfigValidator.Validate(config);

        if (errors.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✓ Config is valid.[/]");
            return Task.FromResult(0);
        }

        foreach (var error in errors)
        {
            AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(error)}");
        }

        return Task.FromResult(1);
    }
}