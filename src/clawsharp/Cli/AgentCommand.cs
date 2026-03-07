using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Spectre.Console.Cli;

namespace Clawsharp.Cli;

/// <summary>
///     Default command — no args starts the gateway host; -m sends a single-shot message and exits.
/// </summary>
[UsedImplicitly]
public sealed class AgentCommand : AsyncCommand<AgentCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-m|--message")]
        [Description("Single-shot: send one message and exit (no session, no tools)")]
        public string? Message { get; init; }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification =
            "Spectre.Console.Cli already requires unreferenced code. EF Core DbContext types are statically rooted in this project.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Spectre.Console.Cli already requires dynamic code. EF Core types are statically rooted in this project.")]
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Message is not null)
        {
            await SingleShotCommand.RunAsync(settings.Message, cancellationToken);
            return 0;
        }

        await GatewayHost.RunAsync(cancellationToken);
        return 0;
    }
}