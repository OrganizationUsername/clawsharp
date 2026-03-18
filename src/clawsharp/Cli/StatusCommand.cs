using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Memory;

namespace Clawsharp.Cli;

/// <summary>Prints a summary of the current configuration, session counts, and token totals.</summary>
[UsedImplicitly]
public sealed class StatusCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();

        AnsiConsole.MarkupLine("[bold]clawsharp status[/]");
        AnsiConsole.WriteLine();

        // Agent defaults
        var d = config.Agents.Defaults;
        AnsiConsole.MarkupLine($"[cyan]Provider[/]       : {Markup.Escape(d.Provider)}");
        AnsiConsole.MarkupLine($"[cyan]Model[/]          : {Markup.Escape(d.Model)}");
        AnsiConsole.MarkupLine($"[cyan]Temperature[/]    : {d.Temperature}");
        AnsiConsole.MarkupLine($"[cyan]Max tool iters[/] : {d.MaxToolIterations}");

        string cachingStatus;
        if (d.Caching?.Enabled == false)
        {
            cachingStatus = "[grey]disabled[/]";
        }
        else if (d.Caching?.CacheToolDefinitions == false)
        {
            cachingStatus = "[green]enabled[/] [grey](system only)[/]";
        }
        else
        {
            cachingStatus = "[green]enabled[/] [grey](system + tools)[/]";
        }

        AnsiConsole.MarkupLine($"[cyan]Caching[/]        : {cachingStatus}");
        AnsiConsole.WriteLine();

        // Memory
        AnsiConsole.MarkupLine($"[cyan]Memory[/]         : {Markup.Escape(config.Memory.Backend)}");
        if (MemoryBackend.TryFromValue(config.Memory.Backend, out var mb) &&
            (mb == MemoryBackend.Markdown || mb == MemoryBackend.Sqlite))
        {
            AnsiConsole.MarkupLine($"  [grey]Dir[/]          : {Markup.Escape(ConfigLoader.ExpandHome(config.Memory.Dir))}");
        }
        else if (config.Memory.ConnectionString is not null)
        {
            AnsiConsole.MarkupLine("  [grey]ConnStr[/]      : [green](set)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [grey]ConnStr[/]      : [yellow](not set)[/]");
        }

        AnsiConsole.WriteLine();

        // Tools
        AnsiConsole.MarkupLine($"[cyan]Workspace[/]      : {Markup.Escape(ConfigLoader.ExpandHome(config.Tools.Workspace))}");
        var braveStatus = config.Tools.Brave?.ApiKey is not null ? "[green](configured)[/]" : "[grey](not configured)[/]";
        AnsiConsole.MarkupLine($"[cyan]Brave[/]          : {braveStatus}");
        AnsiConsole.MarkupLine($"[cyan]Shell approval[/] : {config.Tools.RequireShellApproval}");
        AnsiConsole.WriteLine();

        // Channels
        AnsiConsole.MarkupLine("[cyan]Channels[/]:");
        foreach (var name in ChannelName.List())
        {
            config.Channels.TryGetValue(name.Value, out var ch);
            var enabled = ch?.Enabled == true;
            var stateMarkup = enabled ? "[green]enabled[/] " : "[grey]disabled[/]";
            AnsiConsole.MarkupLine($"  {name.Value,-12} {stateMarkup}");
        }

        AnsiConsole.WriteLine();

        // Token totals (sum across all sessions)
        var (totalIn, totalOut, sessionCount) = await ScanSessionTokensAsync(cancellationToken);
        AnsiConsole.MarkupLine($"[cyan]Sessions[/]       : {sessionCount}");
        if (totalIn > 0 || totalOut > 0)
        {
            AnsiConsole.MarkupLine($"[cyan]Tokens in[/]      : {totalIn:N0}");
            AnsiConsole.MarkupLine($"[cyan]Tokens out[/]     : {totalOut:N0}");
        }
        else
        {
            AnsiConsole.MarkupLine("[cyan]Tokens[/]         : [grey](none recorded yet)[/]");
        }

        return 0;
    }

    private static async Task<(long TotalIn, long TotalOut, int Count)> ScanSessionTokensAsync(CancellationToken ct)
    {
        var sessionsDir = ConfigLoader.ExpandHome("~/.clawsharp/sessions");
        if (!Directory.Exists(sessionsDir))
        {
            return (0, 0, 0);
        }

        var files = Directory.EnumerateFiles(sessionsDir, "*.json").ToArray();
        var results = await Task.WhenAll(files.Select(async file =>
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var session = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Default.Session, ct);
                return session is null ? (0L, 0L, 0) : (session.TotalInputTokens, session.TotalOutputTokens, 1);
            }
            catch
            {
                return (0L, 0L, 0);
            }
        }));

        var totalIn = results.Sum(r => r.Item1);
        var totalOut = results.Sum(r => r.Item2);
        var count = results.Sum(r => r.Item3);
        return (totalIn, totalOut, count);
    }
}