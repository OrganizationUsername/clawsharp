using System.Text.Json;
using System.Text.Json.Nodes;
using Clawsharp.Config;
using Clawsharp.Security;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Config;

/// <summary>Encrypt all plaintext secret fields in config.json.</summary>
[UsedImplicitly]
public sealed class EncryptSecretsCommand : AsyncCommand
{
    // Fields that hold secrets in config.json
    private static readonly IReadOnlySet<string> SecretFields = KnownSecretFields.All;

    public override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var store = new SecretStore(Microsoft.Extensions.Options.Options.Create(config));

        var configPath = ClawsharpConfiguration.GetConfigPath();
        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine("[yellow]No config file found at {0}.[/]", Markup.Escape(configPath));
            return Task.FromResult(1);
        }

        var json = File.ReadAllText(configPath);
        var root = JsonNode.Parse(json) as JsonObject;
        if (root is null)
        {
            AnsiConsole.MarkupLine("[red]Config file is not valid JSON.[/]");
            return Task.FromResult(1);
        }

        var count = EncryptNode(root, store);
        var tempPath = configPath + ".tmp";
        File.WriteAllText(tempPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, configPath, overwrite: true);

        AnsiConsole.MarkupLine($"[green]Encrypted {count} secret field(s) in {Markup.Escape(configPath)}.[/]");
        return Task.FromResult(0);
    }

    private static int EncryptNode(JsonNode node, SecretStore store)
    {
        var count = 0;
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                if (SecretFields.Contains(key) && obj[key] is JsonValue val && val.TryGetValue<string>(out var str))
                {
                    var encrypted = store.Encrypt(str);
                    if (encrypted != str)
                    {
                        obj[key] = encrypted;
                        count++;
                    }
                }
                else if (obj[key] is JsonNode child)
                {
                    count += EncryptNode(child, store);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    count += EncryptNode(item, store);
                }
            }
        }

        return count;
    }
}