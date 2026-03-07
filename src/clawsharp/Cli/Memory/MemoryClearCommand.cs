using System.Diagnostics.CodeAnalysis;
using Clawsharp.Config;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Memory;

/// <summary>Clears all stored memory facts and history.</summary>
[UsedImplicitly]
public sealed class MemoryClearCommand : AsyncCommand
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "MemoryFactory creates EF Core DbContext types that are statically rooted in this project.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "MemoryFactory creates EF Core DbContext types that are statically rooted in this project.")]
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();

        if (!AnsiConsole.Confirm(
                $"[yellow]This will permanently delete all facts and history from the [bold]{config.Memory.Backend}[/] memory backend. Continue?[/]",
                defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        var memory = MemoryFactory.Create(config);
        await memory.ClearAsync(cancellationToken);

        AnsiConsole.MarkupLine("[green]Memory cleared successfully.[/]");
        return 0;
    }
}