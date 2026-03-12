using Clawsharp.Config;
using Clawsharp.Core.Utilities;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Channels;

namespace Clawsharp.Cli.Channel;

/// <summary>Shows enabled/disabled state for all 12 channels.</summary>
[UsedImplicitly]
public sealed class ChannelStatusCommand : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();

        AnsiConsole.MarkupLine("[bold]clawsharp channel status[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("Channel")
                    .AddColumn("State")
                    .AddColumn("Details");

        foreach (var name in ChannelName.List())
        {
            config.Channels.TryGetValue(name.Value, out var ch);
            var enabled = ch?.Enabled == true;
            var stateMarkup = enabled ? "[green]enabled[/]" : "[grey]disabled[/]";
            var details = enabled ? GetDetails(name, ch) : "";
            table.AddRow(new Text(name.Value), new Markup(stateMarkup), new Text(details));
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }

    private static string GetDetails(ChannelName name, ChannelConfig? ch)
    {
        if (ch is null)
        {
            return "";
        }

        if (name == ChannelName.Telegram || name == ChannelName.Discord)
        {
            return ch.Token is not null ? "(token set)" : "(NO TOKEN)";
        }

        if (name == ChannelName.Slack)
        {
            return ch.BotToken is not null && ch.AppToken is not null ? "(tokens set)" : "(tokens missing)";
        }

        if (name == ChannelName.Matrix)
        {
            return ch.Homeserver is not null && ch.AccessToken is not null ? $"({ch.Homeserver})" : "(not configured)";
        }

        if (name == ChannelName.Email)
        {
            return ch.ImapHost is not null && ch.SmtpHost is not null ? $"({ch.ImapHost})" : "(not configured)";
        }

        if (name == ChannelName.Irc)
        {
            return ch.Host is not null ? $"({ch.Host}:{ch.Port})" : "(not configured)";
        }

        if (name == ChannelName.Web)
        {
            return $"({ch.WebHost}:{ch.WebPort})";
        }

        if (name == ChannelName.Signal)
        {
            return ch.BridgeUrl is not null ? $"({ch.BridgeUrl})" : "(not configured)";
        }

        if (name == ChannelName.WhatsApp)
        {
            return ch.BridgeUrl is not null ? $"({ch.BridgeUrl})" : "(not configured)";
        }

        if (name == ChannelName.BlueBubbles)
        {
            return ch.BridgeUrl is not null ? $"({ch.BridgeUrl})" : "(not configured)";
        }

        if (name == ChannelName.Line)
        {
            return ch.Token is not null ? $"(webhook port {ch.LineWebhookPort})" : "(not configured)";
        }

        if (name == ChannelName.WeChat)
        {
            return ch.BridgeUrl is not null ? $"({ch.BridgeUrl})" : ch.WebhookKey is not null ? "(webhook only)" : "(not configured)";
        }

        return "";
    }
}