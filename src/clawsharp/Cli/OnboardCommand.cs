using System.ComponentModel;
using System.Text;
using Clawsharp.Cli.Config;
using Clawsharp.Cli.Skills;
using Clawsharp.Config;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Clawsharp.Config.Agent;

namespace Clawsharp.Cli;

/// <summary>Writes a starter config.json via interactive wizard or command-line flags.</summary>
[UsedImplicitly]
public sealed class OnboardCommand : AsyncCommand<OnboardCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--provider")]
        [Description("LLM provider name (openai, anthropic, ollama, lmstudio, gemini)")]
        public string? Provider { get; init; }

        [CommandOption("-k|--api-key")]
        [Description("API key for the provider")]
        public string? ApiKey { get; init; }

        [CommandOption("-i|--interactive")]
        [Description("Run the interactive onboarding wizard")]
        [DefaultValue(false)]
        public bool Interactive { get; init; }
    }

    /// <summary>A selectable skill shown in the onboarding prompt.</summary>
    /// <param name="Name">Skill identifier (used for install lookup).</param>
    /// <param name="RiskEmoji">🟢 / 🟡 / 🔴 — empty string for group headers.</param>
    /// <param name="Risk">LOW / MEDIUM / HIGH — empty string for group headers.</param>
    /// <param name="Summary">One-line description shown inline in the prompt.</param>
    private sealed record SkillChoice(
        string Name,
        string RiskEmoji = "",
        string Risk = "",
        string Summary = "",
        string RiskDetail = ""
    );

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        LlmProviderType providerType;
        string? apiKey;
        string model;
        IReadOnlyList<string> selectedChannels;
        Dictionary<string, Dictionary<string, string?>> channelCreds;
        List<string> skillsToInstall;

        if (settings.Interactive || (settings.Provider is null && settings.ApiKey is null))
        {
            AnsiConsole.MarkupLine("[bold]clawsharp onboard[/]");
            AnsiConsole.WriteLine();

            // ── Provider ─────────────────────────────────────────────────────────────

            var providerChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Which [cyan]LLM provider[/] do you want to use?")
                    .AddChoices("openai", "anthropic", "ollama", "lmstudio", "gemini"));

            providerType = LlmProviderType.TryFromValue(providerChoice, out var parsed)
                ? parsed
                : LlmProviderType.Ollama;

            model = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Model[/]")
                    .DefaultValue(GetDefaultModel(providerType)));

            apiKey = null;
            if (providerType == LlmProviderType.OpenAi
                || providerType == LlmProviderType.Anthropic
                || providerType == LlmProviderType.Gemini)
            {
                var key = AnsiConsole.Prompt(
                    new TextPrompt<string>($"[cyan]API key[/] for {providerType.Value}")
                        .Secret()
                        .AllowEmpty());
                apiKey = string.IsNullOrEmpty(key) ? null : key;
            }

            // ── Channels ─────────────────────────────────────────────────────────────

            var channelChoices = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Which [cyan]channels[/] do you want to enable? (space to select, enter to confirm)")
                    .NotRequired()
                    .AddChoices("cli", "telegram", "discord", "slack", "matrix", "irc", "web"));

            if (channelChoices.Count == 0)
            {
                channelChoices = ["cli"];
            }
            else if (!channelChoices.Contains("cli"))
            {
                channelChoices.Insert(0, "cli");
            }

            selectedChannels = channelChoices;
            channelCreds = [];

            foreach (var ch in selectedChannels)
            {
                var creds = new Dictionary<string, string?>();
                switch (ch)
                {
                    case "telegram":
                    {
                        var tok = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]Telegram bot token[/]")
                                .Secret().AllowEmpty());
                        if (!string.IsNullOrEmpty(tok))
                        {
                            creds["token"] = tok;
                        }

                        break;
                    }
                    case "discord":
                    {
                        var tok = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]Discord bot token[/]")
                                .Secret().AllowEmpty());
                        if (!string.IsNullOrEmpty(tok))
                        {
                            creds["token"] = tok;
                        }

                        break;
                    }
                    case "slack":
                    {
                        var bot = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]Slack bot token[/]")
                                .Secret().AllowEmpty());
                        var app = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]Slack app token[/]")
                                .Secret().AllowEmpty());
                        if (!string.IsNullOrEmpty(bot))
                        {
                            creds["botToken"] = bot;
                        }

                        if (!string.IsNullOrEmpty(app))
                        {
                            creds["appToken"] = app;
                        }

                        break;
                    }
                    case "matrix":
                    {
                        var hs = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]Matrix homeserver URL[/]")
                                .AllowEmpty());
                        var tok = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]Matrix access token[/]")
                                .Secret().AllowEmpty());
                        if (!string.IsNullOrEmpty(hs))
                        {
                            creds["homeserver"] = hs;
                        }

                        if (!string.IsNullOrEmpty(tok))
                        {
                            creds["accessToken"] = tok;
                        }

                        break;
                    }
                    case "irc":
                    {
                        var host = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]IRC server host[/]")
                                .AllowEmpty());
                        var nick = AnsiConsole.Prompt(
                            new TextPrompt<string>("[cyan]IRC nickname[/]")
                                .AllowEmpty());
                        if (!string.IsNullOrEmpty(host))
                        {
                            creds["host"] = host;
                        }

                        if (!string.IsNullOrEmpty(nick))
                        {
                            creds["nick"] = nick;
                        }

                        break;
                    }
                    // cli, web: no credentials needed
                }

                if (creds.Count > 0)
                {
                    channelCreds[ch] = creds;
                }
            }

            // ── Skills ───────────────────────────────────────────────────────────────

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Skills add behavioral guidelines and capabilities to your agent.[/]");
            AnsiConsole.MarkupLine(
                "[dim][green]skill-vetter[/] is always installed. [green]🟢 LOW[/] = safe, [yellow]🟡 MEDIUM[/] = review notes before installing.[/]");
            AnsiConsole.WriteLine();

            // Group headers — Risk/Summary intentionally empty so converter renders them as plain labels.
            var grpSecurity = new SkillChoice("Security");
            var grpProductivity = new SkillChoice("Productivity");
            var grpMemory = new SkillChoice("Memory");
            var grpDotnet = new SkillChoice(".NET");

            SkillChoice[] securitySkills =
            [
                new("prompt-guard", "🟡", "MEDIUM", "650+ pattern injection defense.",
                    RiskDetail:
                    "Makes optional outbound calls to pg-secure-api.vercel.app to fetch pattern updates (pull-only, no user data sent). Disable with PG_API_ENABLED=false for fully offline use."),
                new("dont-hack-me", "🟡", "MEDIUM", "Config security audit with auto-fix.",
                    RiskDetail:
                    "Reads ~/.clawsharp/config.json (may contain tokens), runs chmod and openssl, and writes config changes on your confirmation."),
            ];
            SkillChoice[] productivitySkills =
            [
                new("self-improvement", "🟢", "LOW", "Logs corrections and learnings across sessions."),
                new("qmd", "🟢", "LOW", "Local file search (BM25 + vector). Requires qmd CLI."),
                new("brave-search", "🟡", "MEDIUM", "Web search + page extraction via Brave.",
                    RiskDetail:
                    "Scrapes search.brave.com with a fake browser User-Agent and fetches arbitrary URLs. Requires Node.js and npm ci after install."),
                new("proactive-research", "🟡", "MEDIUM", "Scheduled topic monitoring with smart alerts.",
                    RiskDetail:
                    "Writes cron jobs to your system scheduler for automated monitoring. Posts alerts to user-configured Discord webhooks or email SMTP. Requires Python 3.8+ and requests library."),
            ];
            SkillChoice[] memorySkills =
            [
                new("supermemory", "🟡", "MEDIUM", "Cloud memory store via SuperMemory API.",
                    RiskDetail:
                    "Every memory you store is sent to api.supermemory.ai. Requires SUPERMEMORY_API_KEY env var. Data leaves your machine."),
            ];
            SkillChoice[] dotnetSkills =
            [
                new("dotnet", "🟢", "LOW", "Core C#/.NET coding skills (scripts, P/Invoke, NuGet)."),
                new("dotnet-data", "🟢", "LOW", "EF Core and data access skills."),
                new("dotnet-diag", "🟢", "LOW", "Performance investigation, debugging, and diagnostics."),
                new("dotnet-msbuild", "🟢", "LOW", "Build failure diagnosis, perf optimization, modernization."),
                new("dotnet-upgrade", "🟢", "LOW", "Migration and upgrade across .NET versions."),
            ];

            var extraSkills = AnsiConsole.Prompt(
                new MultiSelectionPrompt<SkillChoice>()
                    .Title("Additional [cyan]skills[/] to install?")
                    .NotRequired()
                    .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                    .UseConverter(s => string.IsNullOrEmpty(s.Risk)
                        ? s.Name
                        : string.IsNullOrEmpty(s.RiskDetail)
                            ? $"{s.Name,-20} {s.RiskEmoji} {s.Risk,-8}  {s.Summary}"
                            : $"{s.Name,-20} {s.RiskEmoji} {s.Risk,-8}  {s.Summary}  |  ⚠ {s.RiskDetail}")
                    .AddChoiceGroup(grpSecurity, securitySkills)
                    .AddChoiceGroup(grpProductivity, productivitySkills)
                    .AddChoiceGroup(grpMemory, memorySkills)
                    .AddChoiceGroup(grpDotnet, dotnetSkills));

            skillsToInstall = ["skill-vetter"];
            foreach (var s in extraSkills)
            {
                if (!skillsToInstall.Contains(s.Name, StringComparer.Ordinal))
                {
                    skillsToInstall.Add(s.Name);
                }
            }
        }
        else
        {
            providerType = LlmProviderType.TryFromValue(settings.Provider ?? "ollama", out var parsed)
                ? parsed
                : LlmProviderType.Ollama;
            apiKey = settings.ApiKey;
            model = GetDefaultModel(providerType);
            selectedChannels = ["cli"];
            channelCreds = [];
            skillsToInstall = ["skill-vetter"];
        }

        // ── Install skills ───────────────────────────────────────────────────────

        AnsiConsole.WriteLine();
        await SkillRegistry.InstallSkillsAsync(skillsToInstall, cancellationToken);

        // ── Write config ─────────────────────────────────────────────────────────

        var configPath = ConfigLoader.ExpandHome("~/.clawsharp/config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        // Auto-generate (or load) the encryption key before writing so all
        // secret fields are stored as enc2: ciphertext from the very first run.
        var store = new SecretStore(Options.Create(new AppConfig()));
        var json = BuildConfigJson(providerType, model, apiKey, selectedChannels, channelCreds, store);
        await File.WriteAllTextAsync(configPath, json, cancellationToken);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Config written to:[/] {Markup.Escape(configPath)}");
        AnsiConsole.MarkupLine($"  [cyan]Provider[/] : {providerType.Value}");
        AnsiConsole.MarkupLine($"  [cyan]Model[/]    : {Markup.Escape(model)}");
        if (apiKey is not null)
        {
            AnsiConsole.MarkupLine("  [cyan]API key[/]  : [green](set)[/]");
        }

        AnsiConsole.MarkupLine($"  [cyan]Channels[/] : {string.Join(", ", selectedChannels)}");
        AnsiConsole.MarkupLine($"  [cyan]Skills[/]   : {string.Join(", ", skillsToInstall)}");

        // ── P4: open-access channel warnings ────────────────────────────────────
        PrintOpenAccessWarnings(selectedChannels, channelCreds);
        PrintChannelSecurityAdvisories(selectedChannels);

        // ── Secrets security advisor ─────────────────────────────────────────
        PrintSecretsSecurityAdvisor(providerType, selectedChannels, apiKey is not null);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Run [cyan]clawsharp[/] to start the gateway.");
        AnsiConsole.MarkupLine("[dim]Tip: run inside Docker or Podman for an isolated, safer environment.[/]");
        return 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prints yellow warnings for any enabled channel that has no allowFrom / allowedChannels
    /// configured, meaning the bot will respond to anyone who can reach it.
    /// </summary>
    private static void PrintOpenAccessWarnings(
        IReadOnlyList<string> channels,
        Dictionary<string, Dictionary<string, string?>> channelCreds)
    {
        // Channels where open access is a meaningful risk (cli is localhost-only, not listed)
        string[] warnableChannels = ["telegram", "discord", "slack", "matrix", "email", "irc"];

        var openChannels = channels
                           .Where(ch => warnableChannels.Contains(ch, StringComparer.Ordinal))
                           .ToList();

        if (openChannels.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]⚠  Security notice:[/]");

        foreach (var ch in openChannels)
        {
            var (allowFromKey, extra) = ch switch
            {
                "telegram" => ("allowFrom", "Set allowFrom to your Telegram user ID(s) or @username(s)."),
                "discord" => ("allowFrom", "Set allowFrom to Discord user IDs, allowedChannels to guild IDs."),
                "slack" => ("allowFrom", "Set allowFrom to Slack user IDs, allowedChannels to channel IDs."),
                "matrix" => ("allowFrom", "Set allowFrom to Matrix MXIDs (e.g. @you:matrix.org), allowRooms to room IDs."),
                "email" => ("allowFrom", "Set allowFrom to trusted sender addresses, or commandPrefix to gate by subject."),
                "irc" => ("allowFrom", "Set allowFrom to trusted nicks, allowedChannels to allowed IRC channels."),
                _ => ("allowFrom", string.Empty),
            };

            // If the wizard collected no creds (or no allowFrom was set), warn
            _ = allowFromKey; // used for documentation; config currently writes no allowFrom
            AnsiConsole.MarkupLine($"  [yellow]■[/] [bold]{ch}[/] is open — any user can interact with your bot.");
            if (!string.IsNullOrEmpty(extra))
            {
                AnsiConsole.MarkupLine($"    [dim]{Markup.Escape(extra)}[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Edit config.json and add allowFrom / allowedChannels to restrict access.[/]");
        AnsiConsole.MarkupLine("[dim]Run [/][cyan]clawsharp config validate[/][dim] to check your settings.[/]");
    }

    /// <summary>
    /// Prints protocol-level security advisories for channels with known authentication weaknesses.
    /// These appear regardless of whether allowFrom is configured.
    /// </summary>
    private static void PrintChannelSecurityAdvisories(IReadOnlyList<string> channels)
    {
        var hasIrc = channels.Contains("irc", StringComparer.Ordinal);
        var hasEmail = channels.Contains("email", StringComparer.Ordinal);

        if (!hasIrc && !hasEmail)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[red]⚠  Protocol-level security warnings:[/]");

        if (hasIrc)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold red]IRC — nick-based authentication is soft security[/]");
            AnsiConsole.MarkupLine("  [dim]IRC nicks are not authenticated by default. Any user can change their nick[/]");
            AnsiConsole.MarkupLine("  [dim]to match a name on your allowFrom list and impersonate a trusted user.[/]");
            AnsiConsole.MarkupLine("  [dim]The allowFrom filter blocks casual abuse but not a determined attacker.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [dim]To harden IRC:[/]");
            AnsiConsole.MarkupLine("  [dim]  • Use a private IRC server with mandatory NickServ identification[/]");
            AnsiConsole.MarkupLine("  [dim]  • Or implement WHOX / RPL_WHOISACCOUNT (330) to match on account[/]");
            AnsiConsole.MarkupLine("  [dim]    names instead of raw nicks (requires additional IRC protocol work)[/]");
            AnsiConsole.MarkupLine("  [dim]  • Best option: keep IRC disabled unless you control the network[/]");
        }

        if (hasEmail)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold yellow]Email — From: headers are trivially spoofable[/]");
            AnsiConsole.MarkupLine("  [dim]clawsharp uses the envelope sender (Sender: header, then MAIL FROM:)[/]");
            AnsiConsole.MarkupLine("  [dim]rather than the From: display header, which is harder to spoof.[/]");
            AnsiConsole.MarkupLine("  [dim]For stronger guarantees, configure DKIM and SPF at your mail server[/]");
            AnsiConsole.MarkupLine("  [dim]and reject mail that fails both checks before it reaches clawsharp.[/]");
        }
    }

    /// <summary>
    /// Prints a concise security advisor covering secret encryption, key backup,
    /// token scoping, rotation cadence, and password manager integration.
    /// Shown once after initial onboarding so users understand how to keep their
    /// API keys safe before they share the config with anyone.
    /// </summary>
    private static void PrintSecretsSecurityAdvisor(
        LlmProviderType provider,
        IReadOnlyList<string> channels,
        bool hasApiKey)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]── Secrets Security Advisor ──────────────────────────────────────[/]");

        // ── 1. Encryption status ──────────────────────────────────────────────
        AnsiConsole.WriteLine();
        if (hasApiKey)
        {
            AnsiConsole.MarkupLine("[green]✓[/] Your API key is encrypted at rest with [bold]ChaCha20-Poly1305[/] ([dim]enc2:[/] prefix).");
            AnsiConsole.MarkupLine("  Channel tokens entered during setup are also encrypted.");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]■[/] No API key entered — nothing to encrypt yet.");
            AnsiConsole.MarkupLine("  When you add a key later, run [cyan]clawsharp config encrypt-secrets[/] to secure it.");
        }

        var keyPath = ConfigLoader.ExpandHome("~/.clawsharp/.secret_key");
        AnsiConsole.MarkupLine($"  Encryption key: [dim]{Markup.Escape(keyPath)}[/]  (chmod 600)");

        // ── 2. Back up the key ────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]▸ BACK UP YOUR ENCRYPTION KEY[/]");
        AnsiConsole.MarkupLine("  [dim]The key file is separate from config.json. If you lose it, every[/]");
        AnsiConsole.MarkupLine("  [dim]enc2: value in config.json becomes permanently unreadable.[/]");
        AnsiConsole.MarkupLine($"  [dim]cp {Markup.Escape(keyPath)} ~/backup/.clawsharp.secret_key[/]");
        AnsiConsole.MarkupLine("  [dim]Store the backup copy in your password manager's secure notes.[/]");

        // ── 3. Scope your tokens ──────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]▸ SCOPE YOUR SECRETS (principle of least privilege)[/]");

        // Provider-specific scoping advice
        if (provider == LlmProviderType.OpenAi)
        {
            AnsiConsole.MarkupLine("  [dim]OpenAI: create a project-scoped key instead of an org key.[/]");
            AnsiConsole.MarkupLine("  [dim]  • Set a monthly spend limit in platform.openai.com → Settings → Limits[/]");
            AnsiConsole.MarkupLine("  [dim]  • Restrict to specific models if you only use one (e.g. gpt-4o-mini)[/]");
            AnsiConsole.MarkupLine("  [dim]  • Use separate keys for dev and production[/]");
        }
        else if (provider == LlmProviderType.Anthropic)
        {
            AnsiConsole.MarkupLine("  [dim]Anthropic: set a monthly usage limit at console.anthropic.com → Settings → Billing[/]");
            AnsiConsole.MarkupLine("  [dim]  • Use separate workspaces / keys for dev and production[/]");
        }
        else if (provider == LlmProviderType.Gemini)
        {
            AnsiConsole.MarkupLine("  [dim]Gemini: restrict your API key in Google Cloud Console → Credentials[/]");
            AnsiConsole.MarkupLine("  [dim]  • Add API restrictions: Generative Language API only[/]");
            AnsiConsole.MarkupLine("  [dim]  • Add application restrictions (HTTP referrer or IP) for server use[/]");
        }

        // Channel-specific scoping advice
        if (channels.Contains("discord", StringComparer.Ordinal))
        {
            AnsiConsole.MarkupLine("  [dim]Discord: in the Developer Portal, enable only the Gateway Intents your bot uses.[/]");
            AnsiConsole.MarkupLine("  [dim]  • MESSAGE_CONTENT requires explicit approval for 100+ server bots[/]");
        }

        if (channels.Contains("slack", StringComparer.Ordinal))
        {
            AnsiConsole.MarkupLine("  [dim]Slack: review OAuth scopes in api.slack.com → Your Apps → OAuth & Permissions[/]");
            AnsiConsole.MarkupLine("  [dim]  • Remove scopes you don't use — each scope is an attack surface[/]");
        }

        if (channels.Contains("telegram", StringComparer.Ordinal))
        {
            AnsiConsole.MarkupLine("  [dim]Telegram: restrict your bot via @BotFather → /mybots → Bot Settings[/]");
            AnsiConsole.MarkupLine("  [dim]  • Disable 'Allow Groups' if you only use DMs[/]");
            AnsiConsole.MarkupLine("  [dim]  • Disable 'Inline Mode' if not used[/]");
        }

        // ── 4. Rotation ───────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]▸ ROTATE YOUR SECRETS REGULARLY[/]");
        AnsiConsole.MarkupLine("  [dim]Recommended cadence: every 90 days, or immediately on any suspected exposure.[/]");
        AnsiConsole.MarkupLine("  [dim]When you get a new key, run:[/]");
        AnsiConsole.MarkupLine($"  [dim]  clawsharp config set providers.{provider.Value}.apiKey=<new-key>[/]");
        AnsiConsole.MarkupLine("  [dim]This encrypts the new value automatically. Then revoke the old key at[/]");
        AnsiConsole.MarkupLine("  [dim]the provider dashboard to complete the rotation.[/]");
        AnsiConsole.MarkupLine("  [dim]After any manual edit to config.json, re-encrypt with:[/]");
        AnsiConsole.MarkupLine("  [dim]  clawsharp config encrypt-secrets[/]");

        // ── 5. Password manager integration ───────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]▸ PASSWORD MANAGER INTEGRATION (optional, advanced)[/]");
        AnsiConsole.MarkupLine("  [dim]Instead of storing keys in config.json, reference them from your PM:[/]");
        AnsiConsole.MarkupLine($"  [dim]  \"apiKey\": \"op://Personal/{provider.Value}/credential\"   # 1Password[/]");
        AnsiConsole.MarkupLine($"  [dim]  \"apiKey\": \"bws:<uuid>\"                                 # Bitwarden Secrets Manager[/]");
        AnsiConsole.MarkupLine("  [dim]Benefits: config.json is safe to commit; rotate via PM UI without[/]");
        AnsiConsole.MarkupLine("  [dim]touching config.json; PM audit log tracks every secret access.[/]");
        AnsiConsole.MarkupLine("  [dim]Requires: 1Password CLI (op) or Bitwarden SM CLI (bws) on PATH.[/]");

        // ── 6. Docker ─────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]▸ DOCKER DEPLOYMENT[/]");
        AnsiConsole.MarkupLine("  [dim]Set CLAWSHARP_SECRET_KEY in a .env file (never commit .env):[/]");
        AnsiConsole.MarkupLine("  [dim]  echo CLAWSHARP_SECRET_KEY=$(openssl rand -hex 32) >> .env[/]");
        AnsiConsole.MarkupLine("  [dim]Or use Docker native secrets for production (not visible in docker inspect):[/]");
        AnsiConsole.MarkupLine("  [dim]  openssl rand -hex 32 > clawsharp_secret_key.txt  # add to .gitignore[/]");
        AnsiConsole.MarkupLine("  [dim]  Then uncomment the secrets: block in docker-compose.yml[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]──────────────────────────────────────────────────────────────────[/]");
    }

    private static string GetDefaultModel(LlmProviderType provider)
    {
        if (provider == LlmProviderType.OpenAi)
        {
            return "gpt-4o-mini";
        }

        if (provider == LlmProviderType.Anthropic)
        {
            return "claude-sonnet-4-6";
        }

        if (provider == LlmProviderType.Gemini)
        {
            return "gemini-2.0-flash";
        }

        if (provider == LlmProviderType.LmStudio)
        {
            return "local-model";
        }

        return "llama3.2"; // ollama default
    }

    private static readonly IReadOnlySet<string> SecretFields = KnownSecretFields.All;

    private static string BuildConfigJson(
        LlmProviderType provider, string model, string? apiKey,
        IReadOnlyList<string> channels,
        Dictionary<string, Dictionary<string, string?>> channelCreds,
        SecretStore store)
    {
        var name = provider.Value;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"agents\": {");
        sb.AppendLine("    \"defaults\": {");
        sb.AppendLine($"      \"provider\": \"{name}\",");
        sb.AppendLine($"      \"model\": \"{model}\",");
        sb.AppendLine("      \"temperature\": 0.7,");
        sb.AppendLine("      \"maxToolIterations\": 40");
        sb.AppendLine("    }");
        sb.AppendLine("  },");
        sb.AppendLine("  \"providers\": {");

        if (provider == LlmProviderType.OpenAi
            || provider == LlmProviderType.Anthropic
            || provider == LlmProviderType.Gemini)
        {
            var encApiKey = apiKey is not null ? store.Encrypt(apiKey) : null;
            var key = encApiKey is not null ? $"\"{EscapeJson(encApiKey)}\"" : "\"\"";
            sb.AppendLine($"    \"{name}\": {{ \"type\": \"{name}\", \"apiKey\": {key} }}");
        }
        else
        {
            var baseUrl = provider == LlmProviderType.LmStudio
                ? ClawsharpConstants.LmStudioDefaultBaseUrl
                : ClawsharpConstants.OllamaDefaultBaseUrl;
            sb.AppendLine($"    \"{name}\": {{ \"type\": \"{name}\", \"baseUrl\": \"{baseUrl}\" }}");
        }

        sb.AppendLine("  },");
        sb.AppendLine("  \"channels\": {");

        for (var i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            var comma = i < channels.Count - 1 ? "," : "";
            channelCreds.TryGetValue(ch, out var creds);

            if (creds is null || creds.Count == 0)
            {
                sb.AppendLine($"    \"{ch}\": {{ \"enabled\": true }}{comma}");
            }
            else
            {
                sb.Append($"    \"{ch}\": {{ \"enabled\": true");
                foreach (var (k, v) in creds)
                {
                    if (v is not null)
                    {
                        var encV = SecretFields.Contains(k) ? store.Encrypt(v) : v;
                        sb.Append($", \"{k}\": \"{EscapeJson(encV)}\"");
                    }
                }

                sb.AppendLine($" }}{comma}");
            }
        }

        sb.AppendLine("  },");
        sb.AppendLine("  \"memory\": { \"backend\": \"markdown\" },");
        sb.AppendLine("  \"tools\": { \"workspace\": \"~/.clawsharp/workspace\" }");
        sb.Append("}");
        return sb.ToString();
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}