using Clawsharp.Auth;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Auth;

[UsedImplicitly]
internal sealed class AuthLoginCopilotCommand(GitHubDeviceFlow deviceFlow) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[bold]=== GitHub Copilot Login (Device Flow) ===[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Authorize [cyan]clawsharp[/] to use GitHub Copilot as an LLM provider.");
        AnsiConsole.MarkupLine("Your GitHub account must have an active Copilot subscription.");
        AnsiConsole.WriteLine();

        var token = await deviceFlow.LoginAsync(cancellationToken);
        if (token is null)
        {
            AnsiConsole.MarkupLine("[red]Login failed.[/]");
            return 1;
        }

        await AuthStore.SaveAsync("copilot", token, cancellationToken);
        AnsiConsole.MarkupLine("[green]Logged in to GitHub Copilot successfully.[/]");
        if (token.ExpiresAt.HasValue)
        {
            AnsiConsole.MarkupLine($"[dim]Token expires at {token.ExpiresAt.Value:u} (auto-refreshes).[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("To use Copilot, set your provider in config:");
        AnsiConsole.MarkupLine("  [cyan]clawsharp config set agents.defaults.provider=copilot[/]");
        return 0;
    }
}