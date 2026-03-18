using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Security;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Security;

namespace Clawsharp.Cli.Audit;

[UsedImplicitly]
public sealed class AuditTailCommand : AsyncCommand<AuditTailCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-n|--count")]
        public int Count { get; set; } = 50;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var auditConfig = config.Audit ?? new AuditConfig();

        if (!auditConfig.Enabled)
        {
            AnsiConsole.MarkupLine("[yellow]Audit logging is disabled.[/]");
            return Task.FromResult(0);
        }

        var logPath = ResolveLogPath(auditConfig);

        if (!File.Exists(logPath))
        {
            AnsiConsole.MarkupLine("[grey]No audit log found.[/]");
            return Task.FromResult(0);
        }

        var lines = File.ReadLines(logPath).TakeLast(settings.Count).ToList();
        AnsiConsole.MarkupLine($"[bold]Last {lines.Count} audit events[/]");

        foreach (var line in lines)
        {
            try
            {
                var evt = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEvent);
                if (evt is null)
                {
                    continue;
                }

                string color;
                if (evt.EventType == AuditEventType.PolicyViolation || evt.EventType == AuditEventType.AuthFailure)
                {
                    color = "red";
                }
                else if (evt.EventType == AuditEventType.SecurityEvent)
                {
                    color = "yellow";
                }
                else if (evt.EventType == AuditEventType.CommandExecution)
                {
                    color = "cyan";
                }
                else
                {
                    color = "white";
                }

                AnsiConsole.MarkupLine(
                    $"[grey]{Markup.Escape(evt.Timestamp.ToString("O"))}[/] [[{color}]{Markup.Escape(evt.EventType.Value)}[/{color}]] {Markup.Escape(evt.Action?.Detail ?? evt.Action?.Command ?? "")}");
            }
            catch
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(line)}[/]");
            }
        }

        return Task.FromResult(0);
    }

    internal static string ResolveLogPath(AuditConfig auditConfig)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clawsharp");
        if (string.IsNullOrWhiteSpace(auditConfig.LogPath))
        {
            return Path.Combine(dir, "audit.log");
        }

        if (Path.IsPathRooted(auditConfig.LogPath))
        {
            return auditConfig.LogPath;
        }

        return Path.Combine(dir, auditConfig.LogPath);
    }
}

[UsedImplicitly]
public sealed class AuditSearchCommand : AsyncCommand<AuditSearchCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--event")]
        public string? EventType { get; set; }

        [CommandOption("--channel")]
        public string? Channel { get; set; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var auditConfig = config.Audit ?? new AuditConfig();

        var logPath = AuditTailCommand.ResolveLogPath(auditConfig);

        if (!File.Exists(logPath))
        {
            AnsiConsole.MarkupLine("[grey]No audit log found.[/]");
            return Task.FromResult(0);
        }

        var count = 0;
        foreach (var line in File.ReadLines(logPath))
        {
            try
            {
                var evt = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEvent);
                if (evt is null)
                {
                    continue;
                }

                if (settings.EventType is not null &&
                    !evt.EventType.Value.Equals(settings.EventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (settings.Channel is not null &&
                    !(evt.Actor?.Channel?.Equals(settings.Channel, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                AnsiConsole.MarkupLine(
                    $"[grey]{Markup.Escape(evt.Timestamp.ToString("O"))}[/] [cyan]{Markup.Escape(evt.EventType.Value)}[/] {Markup.Escape(evt.Action?.Detail ?? evt.Action?.Command ?? "")}");
                count++;
            }
            catch
            {
                /* skip malformed lines */
            }
        }

        AnsiConsole.MarkupLine($"[grey]Found {count} matching events.[/]");
        return Task.FromResult(0);
    }
}