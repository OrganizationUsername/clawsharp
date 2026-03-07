using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Pairing;

/// <summary>Lists all pending pairing requests.</summary>
[UsedImplicitly]
public sealed class PairingListCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // CLI commands run outside DI, so instantiate PairingStore directly
        var store = new PairingStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PairingStore>.Instance);

        var pending = await store.GetPendingAsync(cancellationToken);

        if (pending.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No pending pairing requests.[/]");
            return 0;
        }

        var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("Code")
                    .AddColumn("Channel")
                    .AddColumn("Sender ID")
                    .AddColumn("Sender Name")
                    .AddColumn("Expires");

        foreach (var pair in pending)
        {
            table.AddRow(
                new Text(pair.Code),
                new Text(pair.Channel),
                new Text(pair.SenderId),
                new Text(pair.SenderName),
                new Text(pair.ExpiresAt.ToString("O")));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}