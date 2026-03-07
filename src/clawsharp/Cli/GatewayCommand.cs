using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Spectre.Console.Cli;

namespace Clawsharp.Cli;

/// <summary>
///     Alias command that starts the AI agent gateway. Equivalent to running with no arguments.
/// </summary>
[UsedImplicitly]
public sealed class GatewayCommand : AsyncCommand
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification =
            "Spectre.Console.Cli already requires unreferenced code. EF Core DbContext types are statically rooted in this project.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Spectre.Console.Cli already requires dynamic code. EF Core types are statically rooted in this project.")]
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await GatewayHost.RunAsync(cancellationToken);
        return 0;
    }
}