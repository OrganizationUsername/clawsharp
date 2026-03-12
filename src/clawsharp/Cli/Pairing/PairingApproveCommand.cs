using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clawsharp.Config;
using Clawsharp.Core.Sessions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Pairing;

/// <summary>Approves a pending pairing request by 6-digit code and adds the sender to allowFrom.</summary>
[UsedImplicitly]
public sealed class PairingApproveCommand : AsyncCommand<PairingApproveCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<code>")]
        [Description("The 6-digit pairing code to approve")]
        public string Code { get; set; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var store = new PairingStore(
            NullLogger<PairingStore>.Instance);

        var approved = await store.ApproveAsync(settings.Code, cancellationToken);
        if (approved is null)
        {
            AnsiConsole.MarkupLine("[red]Pairing code not found or expired.[/]");
            return 1;
        }

        // Add the sender to the dynamic approved-senders store (takes effect immediately on running gateways).
        var approvedSenders = new ApprovedSendersStore(
            NullLogger<ApprovedSendersStore>.Instance);
        await approvedSenders.AddAsync(approved.Channel, approved.SenderId, cancellationToken);

        // Add the sender ID to channels.{channel}.allowFrom in ~/.clawsharp/config.json
        var configPath = Path.Combine(ConfigLoader.ExpandHome("~/.clawsharp"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        JsonNode? root = null;
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            root = JsonNode.Parse(json);
        }

        root ??= new JsonObject();

        // Navigate to channels.{channel}.allowFrom, creating intermediate objects as needed
        var channelsNode = root["channels"];
        if (channelsNode is null or not JsonObject)
        {
            root["channels"] = new JsonObject();
            channelsNode = root["channels"];
        }

        var channelNode = channelsNode![approved.Channel];
        if (channelNode is null or not JsonObject)
        {
            channelsNode[approved.Channel] = new JsonObject();
            channelNode = channelsNode[approved.Channel];
        }

        var allowFromNode = channelNode!["allowFrom"];
        if (allowFromNode is null or not JsonArray)
        {
            channelNode["allowFrom"] = new JsonArray();
            allowFromNode = channelNode["allowFrom"];
        }

        var allowFromArray = (JsonArray)allowFromNode!;

        // Check if already present
        var alreadyPresent = false;
        foreach (var item in allowFromArray)
        {
            if (item is not null && string.Equals(item.GetValue<string>(), approved.SenderId, StringComparison.Ordinal))
            {
                alreadyPresent = true;
                break;
            }
        }

        if (!alreadyPresent)
        {
            allowFromArray.Add((JsonNode?)JsonValue.Create(approved.SenderId));
        }

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tempPath = configPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, output, cancellationToken);
        File.Move(tempPath, configPath, overwrite: true);

        AnsiConsole.MarkupLine(
            $"[green]Approved[/] {Markup.Escape(approved.SenderName)} ({Markup.Escape(approved.SenderId)}) on [cyan]{Markup.Escape(approved.Channel)}[/]");
        AnsiConsole.MarkupLine(
            $"[grey]Added to approved-senders store and channels.{Markup.Escape(approved.Channel)}.allowFrom in ~/.clawsharp/config.json[/]");
        AnsiConsole.MarkupLine("[grey]The approved-senders store takes effect immediately; config.json change requires a restart.[/]");
        return 0;
    }
}