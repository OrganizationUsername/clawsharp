using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core.Sessions;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Session;

/// <summary>Lists all sessions with message counts and token totals.</summary>
[UsedImplicitly]
public sealed class SessionListCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var sessionsDir = ConfigLoader.ExpandHome("~/.clawsharp/sessions");
        if (!Directory.Exists(sessionsDir))
        {
            AnsiConsole.MarkupLine("[grey]No sessions directory found.[/]");
            return 0;
        }

        var files = Directory.EnumerateFiles(sessionsDir, "*.json")
                             .Select(f => new FileInfo(f))
                             .OrderByDescending(f => f.LastWriteTimeUtc)
                             .ToArray();

        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No sessions found.[/]");
            return 0;
        }

        var results = await Task.WhenAll(files.Select(async fi =>
        {
            try
            {
                await using var stream = File.OpenRead(fi.FullName);
                var session = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Default.Session, cancellationToken);
                if (session is null)
                {
                    return (Name: Path.GetFileNameWithoutExtension(fi.Name), Messages: 0, In: 0L, Out: 0L, Ok: false);
                }

                return (
                    Name: Path.GetFileNameWithoutExtension(fi.Name),
                    Messages: session.TotalMessageCount,
                    In: session.TotalInputTokens,
                    Out: session.TotalOutputTokens,
                    Ok: true
                );
            }
            catch
            {
                return (Name: Path.GetFileNameWithoutExtension(fi.Name), Messages: 0, In: 0L, Out: 0L, Ok: false);
            }
        }));

        var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("Session ID")
                    .AddColumn("Messages")
                    .AddColumn("Tokens In")
                    .AddColumn("Tokens Out");

        foreach (var r in results)
        {
            if (!r.Ok)
            {
                continue;
            }

            table.AddRow(
                new Text(r.Name),
                new Text(r.Messages.ToString("N0")),
                new Text(r.In.ToString("N0")),
                new Text(r.Out.ToString("N0")));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>Clears one or all sessions.</summary>
[UsedImplicitly]
public sealed class SessionClearCommand : AsyncCommand<SessionClearCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[session-id]")]
        public string? SessionId { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var sessionsDir = ConfigLoader.ExpandHome("~/.clawsharp/sessions");
        if (!Directory.Exists(sessionsDir))
        {
            AnsiConsole.MarkupLine("[grey]No sessions directory found.[/]");
            return Task.FromResult(0);
        }

        if (settings.SessionId is { } id)
        {
            if (id.Contains(Path.DirectorySeparatorChar) || id.Contains(Path.AltDirectorySeparatorChar) || id.Contains(".."))
            {
                AnsiConsole.MarkupLine("[red]Invalid session ID.[/]");
                return Task.FromResult(1);
            }

            var path = Path.Combine(sessionsDir, id + ".json");
            var deleted = 0;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted = 1;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return Task.FromResult(1);
            }

            AnsiConsole.MarkupLine($"Deleted {deleted} session(s).");
            return Task.FromResult(0);
        }

        if (!AnsiConsole.Confirm("Clear ALL sessions?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return Task.FromResult(0);
        }

        var files = Directory.EnumerateFiles(sessionsDir, "*.json").ToArray();
        var count = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                count++;
            }
            catch
            {
                /* best-effort */
            }
        }

        AnsiConsole.MarkupLine($"Deleted {count} session(s).");
        return Task.FromResult(0);
    }
}