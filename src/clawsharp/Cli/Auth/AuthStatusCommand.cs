using Clawsharp.Auth;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Auth;

[UsedImplicitly]
internal sealed class AuthStatusCommand : AsyncCommand
{
    private static readonly string[] KnownProviders = ["copilot"];

    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[bold]=== Auth Status ===[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
                    .AddColumn("Provider")
                    .AddColumn("Status")
                    .AddColumn("Expires");

        foreach (var provider in KnownProviders)
        {
            var token = await AuthStore.LoadAsync(provider, cancellationToken);
            if (token is null)
            {
                table.AddRow(provider, "[grey]Not logged in[/]", "-");
                continue;
            }

            string status;
            string expires;
            if (token.IsExpired)
            {
                if (token.RefreshToken is not null)
                {
                    status = "[yellow]Expired (can refresh)[/]";
                }
                else
                {
                    status = "[red]Expired[/]";
                }
                expires = token.ExpiresAt?.ToString("u") ?? "-";
            }
            else
            {
                status = "[green]Active[/]";
                expires = token.ExpiresAt?.ToString("u") ?? "No expiry";
            }

            table.AddRow(Markup.Escape(provider), status, Markup.Escape(expires));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}