using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clawsharp.Config;
using Clawsharp.Security;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Config;

/// <summary>Sets a configuration value by dot-path key in ~/.clawsharp/config.json.</summary>
[UsedImplicitly]
public sealed class ConfigSetCommand : AsyncCommand<ConfigSetCommand.Settings>
{
    // Fields that hold secrets -- values for these keys are auto-encrypted before writing.
    private static readonly IReadOnlySet<string> SecretFields = KnownSecretFields.All;

    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key=value>")]
        [Description("Dot-path key and value (e.g. providers.openai.apiKey=sk-xxx)")]
        public string Assignment { get; set; } = "";

        [CommandOption("--type")]
        [Description("Force value type: string, int, or bool. Skips auto-detection when specified.")]
        public string? Type { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var eqIndex = settings.Assignment.IndexOf('=');
        if (eqIndex < 1)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] clawsharp config set <key=value>");
            AnsiConsole.MarkupLine("[grey]Example: clawsharp config set providers.openai.apiKey=sk-xxx[/]");
            return 1;
        }

        var key = settings.Assignment[..eqIndex];
        var value = settings.Assignment[(eqIndex + 1)..];
        var segments = key.Split('.');

        if (segments.Length == 0 || segments.Any(s => s.Length == 0))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Invalid key: {Markup.Escape(key)}");
            return 1;
        }

        if (!ConfigKeyValidator.IsValidKey(key))
        {
            AnsiConsole.MarkupLine(
                $"[red]Error:[/] Unknown config key '[cyan]{Markup.Escape(key)}[/]' — not a recognised AppConfig path. No changes were written.");
            return 1;
        }

        var configPath = Path.Combine(ConfigLoader.ExpandHome("~/.clawsharp"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        JsonNode? root = null;
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            root = JsonNode.Parse(json);
        }

        root ??= new JsonObject();

        // Navigate to the parent node, creating intermediate objects as needed.
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            if (current is not JsonObject obj)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] Cannot navigate into non-object at '{Markup.Escape(string.Join('.', segments[..i]))}'.");
                return 1;
            }

            if (obj[seg] is null or not JsonObject)
            {
                obj[seg] = new JsonObject();
            }

            current = obj[seg];
        }

        if (current is not JsonObject parentObj)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Cannot set value: parent is not an object.");
            return 1;
        }

        // Auto-encrypt secret fields before writing.
        var leafKey = segments[^1];
        if (SecretFields.Contains(leafKey) && !value.StartsWith("enc2:", StringComparison.Ordinal))
        {
            var appConfig = ClawsharpConfiguration.GetAppConfig();
            var store = new SecretStore(Microsoft.Extensions.Options.Options.Create(appConfig));
            value = store.Encrypt(value);
        }

        var typed = DetectTypedValue(value, settings.Type);
        if (typed is null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Cannot parse '{Markup.Escape(value)}' as {Markup.Escape(settings.Type!)}.");
            return 1;
        }

        parentObj[segments[^1]] = typed;

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tempPath = configPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, output, cancellationToken);
        File.Move(tempPath, configPath, overwrite: true);

        AnsiConsole.MarkupLine($"[green]Set[/] [cyan]{Markup.Escape(key)}[/] in [grey]~/.clawsharp/config.json[/]");
        return 0;
    }

    /// <summary>
    /// Parses a raw string into a typed <see cref="JsonNode"/>.
    /// When <paramref name="typeOverride"/> is specified ("string", "int", or "bool"),
    /// the value is forced to that type — returns <c>null</c> if parsing fails.
    /// When <paramref name="typeOverride"/> is <c>null</c>, auto-detects with precedence:
    /// bool -> int -> string.
    /// </summary>
    internal static JsonNode? DetectTypedValue(string value, string? typeOverride = null)
    {
        if (typeOverride is not null)
        {
            return typeOverride.ToLowerInvariant() switch
            {
                "string" => JsonValue.Create(value),
                "int" when int.TryParse(value, out var i) => JsonValue.Create(i),
                "bool" when bool.TryParse(value, out var b) => JsonValue.Create(b),
                "int" or "bool" => null, // parse failed
                _ => null // unrecognised type name
            };
        }

        // Auto-detect: bool only — everything else stays as string to avoid
        // corrupting API keys and other numeric-looking string fields.
        // Use exact match (no whitespace tolerance) to avoid unintended coercion.
        if (bool.TryParse(value, out var boolValue) && value.Length == value.AsSpan().Trim().Length)
        {
            return JsonValue.Create(boolValue);
        }

        return JsonValue.Create(value)!;
    }
}