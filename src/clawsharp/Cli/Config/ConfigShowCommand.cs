using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Memory;

namespace Clawsharp.Cli.Config;

/// <summary>Prints the resolved configuration with secrets redacted.</summary>
[UsedImplicitly]
public sealed class ConfigShowCommand : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();

        AnsiConsole.MarkupLine("[bold]clawsharp config show[/]");
        AnsiConsole.WriteLine();

        // Agent defaults
        var d = config.Agents.Defaults;
        AnsiConsole.MarkupLine($"[cyan]Provider[/]         : {Markup.Escape(d.Provider)}");
        AnsiConsole.MarkupLine($"[cyan]Model[/]            : {Markup.Escape(d.Model)}");
        AnsiConsole.MarkupLine($"[cyan]Temperature[/]      : {d.Temperature}");
        AnsiConsole.MarkupLine($"[cyan]Max tool iters[/]   : {d.MaxToolIterations}");
        AnsiConsole.MarkupLine($"[cyan]Max context msgs[/] : {d.MaxContextMessages}");
        AnsiConsole.MarkupLine($"[cyan]Consolidate every[/]: {d.ConsolidateEvery}");
        AnsiConsole.MarkupLine($"[cyan]Rate limit[/]       : {d.RateLimitRequests} / {d.RateLimitWindowSeconds}s");
        AnsiConsole.WriteLine();

        // Providers
        AnsiConsole.MarkupLine("[cyan]Providers[/]:");
        foreach (var (name, p) in config.Providers)
        {
            var keyDisplay = Redact(p.ApiKey);
            var urlDisplay = p.BaseUrl is not null ? $", baseUrl={Markup.Escape(p.BaseUrl)}" : "";
            AnsiConsole.MarkupLine($"  {Markup.Escape(name),-14} type={Markup.Escape(p.Type)}, apiKey={keyDisplay}{urlDisplay}");
        }

        AnsiConsole.WriteLine();

        // Memory
        AnsiConsole.MarkupLine($"[cyan]Memory backend[/]   : {Markup.Escape(config.Memory.Backend)}");
        if (MemoryBackend.TryFromValue(config.Memory.Backend, out var mb) &&
            (mb == MemoryBackend.Markdown || mb == MemoryBackend.Sqlite))
        {
            AnsiConsole.MarkupLine($"  [grey]Dir[/]            : {Markup.Escape(ConfigLoader.ExpandHome(config.Memory.Dir))}");
        }

        if (config.Memory.ConnectionString is not null)
        {
            AnsiConsole.MarkupLine("  [grey]ConnStr[/]        : (set)");
        }
        else if (mb == MemoryBackend.Postgres || mb == MemoryBackend.MsSql)
        {
            AnsiConsole.MarkupLine("  [grey]ConnStr[/]        : [yellow](not set)[/]");
        }

        AnsiConsole.WriteLine();

        // Tools
        AnsiConsole.MarkupLine($"[cyan]Workspace[/]        : {Markup.Escape(ConfigLoader.ExpandHome(config.Tools.Workspace))}");
        AnsiConsole.MarkupLine($"[cyan]Brave API key[/]    : {Redact(config.Tools.Brave?.ApiKey)}");
        AnsiConsole.MarkupLine($"[cyan]Shell approval[/]   : {config.Tools.RequireShellApproval}");
        AnsiConsole.WriteLine();

        // Channels
        AnsiConsole.MarkupLine("[cyan]Channels[/]:");
        foreach (var name in ChannelName.List())
        {
            config.Channels.TryGetValue(name.Value, out var ch);
            var enabled = ch?.Enabled == true;
            var stateMarkup = enabled ? "[green]enabled[/]" : "[grey]disabled[/]";
            AnsiConsole.MarkupLine($"  {name.Value,-12} {stateMarkup}");
        }

        return Task.FromResult(0);
    }

    internal static string Redact(string? s) =>
        s is not null ? "[redacted]" : "(not set)";
}