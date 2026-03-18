using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Migrate;

[UsedImplicitly]
public sealed class MigrateCommand : AsyncCommand<MigrateCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<source>")]
        [Description("Source project to migrate from: openclaw, picoclaw, or zeroclaw")]
        public string Source { get; init; } = "";

        [CommandOption("--dry-run")]
        [Description("Preview the migration without writing to disk")]
        public bool DryRun { get; init; }

        [CommandOption("--force")]
        [Description("Overwrite existing clawsharp config without prompting")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext ctx, Settings settings, CancellationToken cancellation)
    {
        var supportedSources = new[] { "openclaw", "picoclaw", "zeroclaw" };
        if (!supportedSources.Any(s => s.Equals(settings.Source, StringComparison.OrdinalIgnoreCase)))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Unknown source. Supported: openclaw, picoclaw, zeroclaw");
            return 1;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var sourceConfig = settings.Source.ToLowerInvariant() switch
        {
            "picoclaw" => Path.Combine(home, ".picoclaw", "config.json"),
            "zeroclaw" => Path.Combine(home, ".zeroclaw", "config.toml"),
            _ => Path.Combine(home, ".openclaw", "config.json"),
        };

        var destConfig = Path.Combine(home, ".clawsharp", "config.json");

        return settings.Source.ToLowerInvariant() switch
        {
            "picoclaw" => await MigratePicoClawAsync(settings, sourceConfig, destConfig, cancellation),
            "zeroclaw" => await MigrateZeroClawAsync(settings, sourceConfig, destConfig, cancellation),
            _ => await MigrateOpenClawAsync(settings, sourceConfig, destConfig, cancellation),
        };
    }

    // ── openclaw ──────────────────────────────────────────────────────────────

    private static async Task<int> MigrateOpenClawAsync(
        Settings settings, string sourceConfig, string destConfig, CancellationToken ct)
    {
        if (!File.Exists(sourceConfig))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] openclaw config not found at {sourceConfig}");
            AnsiConsole.MarkupLine("Make sure openclaw is installed and has been configured.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold]Migrating from:[/] {sourceConfig}");
        AnsiConsole.MarkupLine($"[bold]Writing to:[/]    {destConfig}");
        AnsiConsole.WriteLine();

        var sourceText = await File.ReadAllTextAsync(sourceConfig, ct);
        var source = JsonNode.Parse(sourceText);
        if (source is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Could not parse openclaw config.json");
            return 1;
        }

        JsonNode dest;
        if (File.Exists(destConfig))
        {
            var existing = await File.ReadAllTextAsync(destConfig, ct);
            dest = JsonNode.Parse(existing) ?? new JsonObject();
        }
        else
        {
            dest = new JsonObject();
        }

        var warnings = new List<string>();
        var migrated = new List<string>();

        // Agent defaults
        MigrateValue(source, dest, "agents.defaults.model", "agents.defaults.model", migrated);
        MigrateValue(source, dest, "agents.defaults.temperature", "agents.defaults.temperature", migrated);
        MigrateValue(source, dest, "agents.defaults.maxToolIterations", "agents.defaults.maxToolIterations", migrated);
        MigrateValue(source, dest, "agents.defaults.systemPrompt", "agents.defaults.systemPrompt", migrated);

        // Provider rename: "claude" -> "anthropic"
        var providerNode = GetNode(source, "agents.defaults.provider");
        if (providerNode?.GetValue<string>() is { } pname)
        {
            if (pname.Equals("claude", StringComparison.OrdinalIgnoreCase))
            {
                pname = "anthropic";
            }

            SetNode(dest, "agents.defaults.provider", JsonValue.Create(pname)!);
            migrated.Add("agents.defaults.provider");
        }

        // models.providers -> providers
        var modelsProviders = GetNode(source, "models.providers") as JsonObject;
        if (modelsProviders is not null)
        {
            foreach (var (provName, provVal) in modelsProviders)
            {
                if (provVal is JsonObject pObj && pObj["apiKey"]?.GetValue<string>() is { } key)
                {
                    string mappedName;
                    if (provName.Equals("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        mappedName = "anthropic";
                    }
                    else
                    {
                        mappedName = provName;
                    }

                    SetNode(dest, $"providers.{mappedName}.apiKey", JsonValue.Create(key)!);
                    SetNode(dest, $"providers.{mappedName}.type", JsonValue.Create(mappedName)!);
                    migrated.Add($"providers.{mappedName}.apiKey");
                }
            }
        }

        // Channels (direct field mapping)
        foreach (var ch in new[]
                 {
                     "telegram", "discord", "slack", "signal", "whatsapp",
                     "matrix", "email", "irc", "bluebubbles", "line"
                 })
        {
            var chNode = GetNode(source, $"channels.{ch}") as JsonObject;
            if (chNode is null)
            {
                continue;
            }

            foreach (var (field, val) in chNode)
            {
                if (val is null)
                {
                    continue;
                }

                SetNode(dest, $"channels.{ch}.{field}", val.DeepClone());
                migrated.Add($"channels.{ch}.{field}");
            }
        }

        // Tools
        MigrateValue(source, dest, "tools.workspace", "tools.workspace", migrated);
        MigrateValue(source, dest, "tools.brave.apiKey", "tools.brave.apiKey", migrated);

        // Memory
        MigrateValue(source, dest, "memory.backend", "memory.backend", migrated);

        // Cron (array)
        var cronNode = GetNode(source, "cron");
        if (cronNode is JsonArray cronArr && cronArr.Count > 0)
        {
            SetNode(dest, "cron", cronArr.DeepClone());
            migrated.Add($"cron ({cronArr.Count} entries)");
        }

        // Warn about unmigrated openclaw-only features
        if (GetNode(source, "bindings") is not null)
        {
            warnings.Add("'bindings' (channel->agent routing): clawsharp uses a single-agent model -- configure channels directly");
        }

        if (GetNode(source, "canvasHost") is not null)
        {
            warnings.Add("'canvasHost': Canvas/A2UI not available in clawsharp");
        }

        if (GetNode(source, "browser") is not null)
        {
            warnings.Add("'browser': CDP browser tool not available in clawsharp");
        }

        if (GetNode(source, "audio") is not null || GetNode(source, "talk") is not null)
        {
            warnings.Add("'audio'/'talk': Voice features not available in clawsharp");
        }

        if (GetNode(source, "hooks") is not null)
        {
            warnings.Add("'hooks': Plugin hooks not available in clawsharp");
        }

        return await WriteDestConfig(settings, dest, destConfig, migrated, warnings, ct);
    }

    // ── picoclaw ──────────────────────────────────────────────────────────────

    private static async Task<int> MigratePicoClawAsync(
        Settings settings, string sourceConfig, string destConfig, CancellationToken ct)
    {
        if (!File.Exists(sourceConfig))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] picoclaw config not found at {sourceConfig}");
            AnsiConsole.MarkupLine("Make sure picoclaw is installed and has been configured.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold]Migrating from:[/] {sourceConfig}");
        AnsiConsole.MarkupLine($"[bold]Writing to:[/]    {destConfig}");
        AnsiConsole.WriteLine();

        var sourceText = await File.ReadAllTextAsync(sourceConfig, ct);
        var source = JsonNode.Parse(sourceText);
        if (source is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Could not parse picoclaw config.json");
            return 1;
        }

        JsonNode dest;
        if (File.Exists(destConfig))
        {
            var existing = await File.ReadAllTextAsync(destConfig, ct);
            dest = JsonNode.Parse(existing) ?? new JsonObject();
        }
        else
        {
            dest = new JsonObject();
        }

        var warnings = new List<string>();
        var migrated = new List<string>();

        // agents.defaults.model — picoclaw may use an object { primary, fallbacks }
        var modelNode = GetNode(source, "agents.defaults.model");
        if (modelNode is JsonObject modelObj && modelObj["primary"] is JsonNode primaryModel)
        {
            SetNode(dest, "agents.defaults.model", primaryModel.DeepClone());
            migrated.Add("agents.defaults.model");
            warnings.Add(
                "'agents.defaults.model' was an object — only 'primary' was imported; configure fallbacks via 'agents.defaults.fallbackProviders'");
        }
        else if (modelNode is not null)
        {
            SetNode(dest, "agents.defaults.model", modelNode.DeepClone());
            migrated.Add("agents.defaults.model");
        }

        MigrateValue(source, dest, "agents.defaults.temperature", "agents.defaults.temperature", migrated);
        MigrateValue(source, dest, "agents.defaults.maxToolIterations", "agents.defaults.maxToolIterations", migrated);
        MigrateValue(source, dest, "agents.defaults.systemPrompt", "agents.defaults.systemPrompt", migrated);

        // Provider rename: "claude" -> "anthropic"
        var providerNode = GetNode(source, "agents.defaults.provider");
        if (providerNode?.GetValue<string>() is { } pname)
        {
            if (pname.Equals("claude", StringComparison.OrdinalIgnoreCase))
            {
                pname = "anthropic";
            }

            SetNode(dest, "agents.defaults.provider", JsonValue.Create(pname)!);
            migrated.Add("agents.defaults.provider");
        }

        // providers.*
        var providersNode = GetNode(source, "providers") as JsonObject;
        if (providersNode is not null)
        {
            foreach (var (provName, provVal) in providersNode)
            {
                if (provVal is JsonObject pObj && pObj["apiKey"]?.GetValue<string>() is { } key)
                {
                    string mappedName;
                    if (provName.Equals("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        mappedName = "anthropic";
                    }
                    else
                    {
                        mappedName = provName;
                    }

                    SetNode(dest, $"providers.{mappedName}.apiKey", JsonValue.Create(key)!);
                    migrated.Add($"providers.{mappedName}.apiKey");
                }
            }
        }

        // Channels (direct field mapping)
        foreach (var ch in new[]
                 {
                     "telegram", "discord", "slack", "signal", "whatsapp",
                     "matrix", "email", "irc", "bluebubbles", "line"
                 })
        {
            var chNode = GetNode(source, $"channels.{ch}") as JsonObject;
            if (chNode is null)
            {
                continue;
            }

            foreach (var (field, val) in chNode)
            {
                if (val is null)
                {
                    continue;
                }

                SetNode(dest, $"channels.{ch}.{field}", val.DeepClone());
                migrated.Add($"channels.{ch}.{field}");
            }
        }

        // Tools
        MigrateValue(source, dest, "tools.workspace", "tools.workspace", migrated);
        MigrateValue(source, dest, "tools.brave.apiKey", "tools.brave.apiKey", migrated);

        // Memory
        MigrateValue(source, dest, "memory.backend", "memory.backend", migrated);

        // Warn about picoclaw-only features
        if (GetNode(source, "bindings") is not null)
        {
            warnings.Add("'bindings' (channel->agent routing): clawsharp uses a single-agent model");
        }

        if (GetNode(source, "model_list") is not null)
        {
            warnings.Add("'model_list': Model-list config not supported; configure providers directly");
        }

        if (GetNode(source, "devices") is not null)
        {
            warnings.Add("'devices': Device management not available in clawsharp");
        }

        if (GetNode(source, "heartbeat") is JsonObject heartbeatObj)
        {
            foreach (var (field, val) in heartbeatObj)
            {
                if (val is null)
                {
                    continue;
                }

                SetNode(dest, $"agents.defaults.heartbeat.{field}", val.DeepClone());
                migrated.Add($"agents.defaults.heartbeat.{field}");
            }
        }

        if (GetNode(source, "session") is not null)
        {
            warnings.Add("'session': picoclaw session config not applicable; clawsharp manages sessions automatically");
        }

        return await WriteDestConfig(settings, dest, destConfig, migrated, warnings, ct);
    }

    // ── zeroclaw ──────────────────────────────────────────────────────────────

    private static async Task<int> MigrateZeroClawAsync(
        Settings settings, string sourceConfig, string destConfig, CancellationToken ct)
    {
        if (!File.Exists(sourceConfig))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] zeroclaw config not found at {sourceConfig}");
            AnsiConsole.MarkupLine("Make sure zeroclaw is installed and has been configured.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold]Migrating from:[/] {sourceConfig}");
        AnsiConsole.MarkupLine($"[bold]Writing to:[/]    {destConfig}");
        AnsiConsole.WriteLine();

        var tomlText = await File.ReadAllTextAsync(sourceConfig, ct);
        var toml = ParseToml(tomlText);

        JsonNode dest;
        if (File.Exists(destConfig))
        {
            var existing = await File.ReadAllTextAsync(destConfig, ct);
            dest = JsonNode.Parse(existing) ?? new JsonObject();
        }
        else
        {
            dest = new JsonObject();
        }

        var warnings = new List<string>();
        var migrated = new List<string>();

        // agent section → agents.defaults
        MapTomlKey(toml, "agent.model", dest, "agents.defaults.model", migrated);
        MapTomlKey(toml, "agent.system_prompt", dest, "agents.defaults.systemPrompt", migrated);
        MapTomlKey(toml, "agent.max_tool_iterations", dest, "agents.defaults.maxToolIterations", migrated, asInt: true);

        if (toml.TryGetValue("agent.provider", out var agentProvider))
        {
            var pname = agentProvider.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "anthropic" : agentProvider;
            SetNode(dest, "agents.defaults.provider", JsonValue.Create(pname)!);
            migrated.Add("agents.defaults.provider");
        }

        // providers.PROV.api_key → providers.PROV.apiKey
        foreach (var (key, value) in toml)
        {
            if (!key.StartsWith("providers.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = key.Split('.');
            if (parts.Length != 3 || !parts[2].Equals("api_key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var provName = parts[1].Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? "anthropic" : parts[1];
            SetNode(dest, $"providers.{provName}.apiKey", JsonValue.Create(value)!);
            migrated.Add($"providers.{provName}.apiKey");
        }

        // channels.CHAN.token → channels.CHAN.token
        foreach (var (key, value) in toml)
        {
            if (!key.StartsWith("channels.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = key.Split('.');
            if (parts.Length < 3)
            {
                continue;
            }

            var chanName = parts[1];
            var fieldRaw = parts[2];
            // snake_case → camelCase
            var fieldCamel = fieldRaw switch
            {
                "allow_from" => "allowFrom",
                "guild_id" => "guildId",
                "channel_id" => "channelId",
                "bot_token" => "botToken",
                "home_server" => "homeServer",
                "access_token" => "accessToken",
                "phone_number" => "phoneNumber",
                "api_url" => "apiUrl",
                _ => fieldRaw
            };

            // allow_from is a TOML array like ["123","456"] — parse it
            if (fieldRaw == "allow_from")
            {
                var arr = ParseTomlStringArray(value);
                var jsonArr = new JsonArray();
                foreach (var item in arr)
                {
                    jsonArr.Add((JsonNode?)JsonValue.Create(item));
                }

                SetNode(dest, $"channels.{chanName}.{fieldCamel}", jsonArr);
            }
            else
            {
                SetNode(dest, $"channels.{chanName}.{fieldCamel}", JsonValue.Create(value)!);
            }

            migrated.Add($"channels.{chanName}.{fieldCamel}");
        }

        // tools section
        MapTomlKey(toml, "tools.workspace", dest, "tools.workspace", migrated);
        if (toml.TryGetValue("tools.brave_api_key", out var braveKey))
        {
            SetNode(dest, "tools.brave.apiKey", JsonValue.Create(braveKey)!);
            migrated.Add("tools.brave.apiKey");
        }

        return await WriteDestConfig(settings, dest, destConfig, migrated, warnings, ct);
    }

    // ── TOML parser ───────────────────────────────────────────────────────────

    /// <summary>Minimal line-by-line TOML parser for single-level sections (sufficient for zeroclaw config).</summary>
    internal static Dictionary<string, string> ParseToml(string tomlContent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "";

        foreach (var rawLine in tomlContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || line.Length == 0)
            {
                continue;
            }

            // Section header: [section] or [section.subsection]
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var raw = line[(eq + 1)..].Trim();

            // Strip inline comments after value (but preserve # inside quoted strings)
            string value;
            if (raw.StartsWith('"'))
            {
                var closeQuote = raw.IndexOf('"', 1);
                if (closeQuote > 0)
                {
                    value = raw[1..closeQuote];
                }
                else
                {
                    value = raw.Trim('"');
                }
            }
            else if (raw.StartsWith('\''))
            {
                var closeQuote = raw.IndexOf('\'', 1);
                if (closeQuote > 0)
                {
                    value = raw[1..closeQuote];
                }
                else
                {
                    value = raw.Trim('\'');
                }
            }
            else
            {
                // Inline value (number, bool, array literal) — strip trailing comment
                value = raw.Split('#')[0].Trim();
            }

            var fullKey = currentSection.Length > 0 ? $"{currentSection}.{key}" : key;
            result[fullKey] = value;
        }

        return result;
    }

    /// <summary>Parses a TOML inline string array like ["a", "b", "c"].</summary>
    internal static List<string> ParseTomlStringArray(string raw)
    {
        var result = new List<string>();
        var s = raw.Trim().TrimStart('[').TrimEnd(']');
        foreach (var part in s.Split(','))
        {
            var item = part.Trim().Trim('"').Trim('\'');
            if (item.Length > 0)
            {
                result.Add(item);
            }
        }

        return result;
    }

    // ── Shared output ─────────────────────────────────────────────────────────

    private static async Task<int> WriteDestConfig(
        Settings settings, JsonNode dest, string destConfig,
        List<string> migrated, List<string> warnings, CancellationToken ct)
    {
        var table = new Table().Border(TableBorder.Rounded)
                               .AddColumn("Status").AddColumn("Count");
        table.AddRow("[green]Migrated[/]", migrated.Count.ToString());
        table.AddRow("[yellow]Warnings[/]", warnings.Count.ToString());
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (migrated.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold green]Migrated:[/]");
            foreach (var m in migrated)
            {
                AnsiConsole.MarkupLine($"  [green]>[/] {m}");
            }
        }

        if (warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]Manual attention required:[/]");
            foreach (var w in warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]![/] {w}");
            }
        }

        if (settings.DryRun)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim](dry run -- no changes written)[/]");
            return 0;
        }

        if (!settings.Force && File.Exists(destConfig))
        {
            AnsiConsole.WriteLine();
            if (!AnsiConsole.Confirm($"Merge into existing {destConfig}?"))
            {
                AnsiConsole.MarkupLine("[yellow]Aborted.[/]");
                return 1;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destConfig)!);
        var destText = dest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(destConfig, destText, ct);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold green]Config written to {destConfig}[/]");
        AnsiConsole.MarkupLine("Run [bold]clawsharp config validate[/] to check for issues.");
        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void MapTomlKey(
        Dictionary<string, string> toml,
        string tomlKey,
        JsonNode dest,
        string destPath,
        List<string> migrated,
        bool asInt = false)
    {
        if (!toml.TryGetValue(tomlKey, out var value))
        {
            return;
        }

        JsonNode node = asInt && int.TryParse(value, out var iv) ? JsonValue.Create(iv)! : JsonValue.Create(value)!;
        SetNode(dest, destPath, node);
        migrated.Add(destPath);
    }

    internal static JsonNode? GetNode(JsonNode root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current))
            {
                return null;
            }
        }

        return current;
    }

    internal static void SetNode(JsonNode root, string path, JsonNode value)
    {
        var parts = path.Split('.');
        var current = root as JsonObject ?? throw new InvalidOperationException("Root must be JsonObject");
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetPropertyValue(parts[i], out var next) || next is not JsonObject nextObj)
            {
                nextObj = new JsonObject();
                current[parts[i]] = nextObj;
            }

            current = nextObj;
        }

        current[parts[^1]] = value;
    }

    private static void MigrateValue(JsonNode source, JsonNode dest, string srcPath, string dstPath, List<string> migrated)
    {
        var node = GetNode(source, srcPath);
        if (node is null)
        {
            return;
        }

        SetNode(dest, dstPath, node.DeepClone());
        migrated.Add(dstPath);
    }
}