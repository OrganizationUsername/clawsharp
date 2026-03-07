using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Security;
using Spectre.Console;

namespace Clawsharp.Cli.Skills;

/// <summary>Skill install strategies.</summary>
public enum SkillSource
{
    BuiltIn,

    GitClone,

    GitHubApi
}

/// <summary>Where and how a skill is obtained.</summary>
public sealed record SkillEntry(
    SkillSource Source,
    string? CloneUrl = null,
    string? GitHubRepo = null,
    string? GitHubPath = null
);

/// <summary>A skill shown in the search/onboarding prompt.</summary>
public sealed record SkillInfo(
    string Name,
    string Group,
    string Risk,
    string Description
);

/// <summary>
/// Shared skill catalogue and installation logic used by both OnboardCommand and
/// the skills CLI commands (list/search/install/remove).
/// </summary>
public static class SkillRegistry
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        // SSRF defense-in-depth: downloads follow GitHub API download_url values,
        // re-validate resolved IPs at TCP connect time to close the DNS rebinding gap.
        ConnectCallback = SsrfGuard.CreateConnectCallback()
    })
    {
        DefaultRequestHeaders = { { "User-Agent", "clawsharp/1.0" } }
    };

    /// <summary>Skills directory: ~/.clawsharp/skills/</summary>
    public static string SkillsDir => ConfigLoader.ExpandHome("~/.clawsharp/skills");

    /// <summary>
    /// Curated skill catalogue. Source determines install strategy:
    ///   BuiltIn   -- content embedded as C# constant, no network required
    ///   GitClone  -- shallow-clone a standalone GitHub repo
    ///   GitHubApi -- download a subdirectory from a monorepo via GitHub API
    /// </summary>
    public static readonly Dictionary<string, SkillEntry> KnownSkills = new(StringComparer.Ordinal)
    {
        // always installed (built-in)
        ["skill-vetter"] = new(SkillSource.BuiltIn),

        // security
        ["prompt-guard"] = new(SkillSource.GitClone, CloneUrl: "https://github.com/seojoonkim/prompt-guard.git"),
        ["dont-hack-me"] = new(SkillSource.BuiltIn),

        // productivity
        ["self-improvement"] = new(SkillSource.GitClone, CloneUrl: "https://github.com/peterskoett/self-improving-agent.git"),
        ["qmd"] = new(SkillSource.BuiltIn),
        ["brave-search"] = new(SkillSource.GitHubApi, GitHubRepo: "openclaw/skills", GitHubPath: "skills/steipete/brave-search"),
        ["proactive-research"] = new(SkillSource.GitHubApi, GitHubRepo: "openclaw/skills",
            GitHubPath: "skills/robbyczgw-cla/proactive-research"),

        // memory
        ["supermemory"] = new(SkillSource.GitHubApi, GitHubRepo: "openclaw/skills", GitHubPath: "skills/clawdbot51-oss/supermemory"),

        // .NET (dotnet/skills)
        ["dotnet"] = new(SkillSource.GitHubApi, GitHubRepo: "dotnet/skills", GitHubPath: "plugins/dotnet"),
        ["dotnet-data"] = new(SkillSource.GitHubApi, GitHubRepo: "dotnet/skills", GitHubPath: "plugins/dotnet-data"),
        ["dotnet-diag"] = new(SkillSource.GitHubApi, GitHubRepo: "dotnet/skills", GitHubPath: "plugins/dotnet-diag"),
        ["dotnet-msbuild"] = new(SkillSource.GitHubApi, GitHubRepo: "dotnet/skills", GitHubPath: "plugins/dotnet-msbuild"),
        ["dotnet-upgrade"] = new(SkillSource.GitHubApi, GitHubRepo: "dotnet/skills", GitHubPath: "plugins/dotnet-upgrade"),
    };

    /// <summary>Searchable skill metadata with descriptions and risk levels.</summary>
    public static readonly SkillInfo[] Catalogue =
    [
        new("skill-vetter", "Security", "LOW", "Security-first skill vetting for AI agents."),
        new("prompt-guard", "Security", "MEDIUM", "650+ pattern injection defense."),
        new("dont-hack-me", "Security", "MEDIUM", "Config security audit with auto-fix."),
        new("self-improvement", "Productivity", "LOW", "Logs corrections and learnings across sessions."),
        new("qmd", "Productivity", "LOW", "Local file search (BM25 + vector). Requires qmd CLI."),
        new("brave-search", "Productivity", "MEDIUM", "Web search + page extraction via Brave."),
        new("proactive-research", "Productivity", "MEDIUM", "Scheduled topic monitoring with smart alerts."),
        new("supermemory", "Memory", "MEDIUM", "Cloud memory store via SuperMemory API."),
        new("dotnet", ".NET", "LOW", "Core C#/.NET coding skills (scripts, P/Invoke, NuGet)."),
        new("dotnet-data", ".NET", "LOW", "EF Core and data access skills."),
        new("dotnet-diag", ".NET", "LOW", "Performance investigation, debugging, and diagnostics."),
        new("dotnet-msbuild", ".NET", "LOW", "Build failure diagnosis, perf optimization, modernization."),
        new("dotnet-upgrade", ".NET", "LOW", "Migration and upgrade across .NET versions."),
    ];

    // ── Installation ────────────────────────────────────────────────────────────

    public static async Task InstallSkillAsync(string skill, CancellationToken ct)
    {
        var skillsDir = SkillsDir;
        Directory.CreateDirectory(skillsDir);

        var destDir = Path.Combine(skillsDir, skill);
        if (Directory.Exists(destDir))
        {
            AnsiConsole.MarkupLine($"  {Markup.Escape(skill)} is already installed.");
            return;
        }

        if (!KnownSkills.TryGetValue(skill, out var entry))
        {
            AnsiConsole.MarkupLine($"[red]  Unknown skill:[/] {Markup.Escape(skill)}");
            return;
        }

        switch (entry.Source)
        {
            case SkillSource.BuiltIn:
                await WriteBuiltInSkillAsync(skill, destDir, ct);
                break;
            case SkillSource.GitClone:
                await GitCloneSkillAsync(skill, entry.CloneUrl!, destDir);
                break;
            case SkillSource.GitHubApi:
                await GitHubApiDownloadAsync(skill, entry.GitHubRepo!, entry.GitHubPath!, destDir, ct);
                break;
        }
    }

    public static async Task InstallSkillsAsync(IReadOnlyList<string> skills, CancellationToken ct)
    {
        foreach (var skill in skills)
        {
            await InstallSkillAsync(skill, ct);
        }
    }

    private static async Task WriteBuiltInSkillAsync(string skill, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var content = skill switch
        {
            "skill-vetter" => SkillVetterContent,
            "dont-hack-me" => DontHackMeContent,
            "qmd" => QmdContent,
            _ => null,
        };
        if (content is null)
        {
            return;
        }

        await File.WriteAllTextAsync(Path.Combine(destDir, "SKILL.md"), content, ct);
        AnsiConsole.MarkupLine($"  Installed {Markup.Escape(skill)} (built-in)");
    }

    private static async Task GitCloneSkillAsync(string skill, string repoUrl, string destDir)
    {
        AnsiConsole.Markup($"  Installing {Markup.Escape(skill)}... ");
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("clone");
            startInfo.ArgumentList.Add("--depth");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add(repoUrl);
            startInfo.ArgumentList.Add(destDir);
            using var proc = Process.Start(startInfo);
            if (proc is null)
            {
                throw new InvalidOperationException("Failed to start git");
            }

            await proc.WaitForExitAsync();
            if (proc.ExitCode == 0)
            {
                AnsiConsole.MarkupLine("[green]done[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]failed[/]");
                AnsiConsole.MarkupLine($"    Run: git clone {Markup.Escape(repoUrl)} ~/.clawsharp/skills/{Markup.Escape(skill)}");
            }
        }
        catch
        {
            AnsiConsole.MarkupLine("[yellow]skipped (git not found)[/]");
            AnsiConsole.MarkupLine($"    Run: git clone {Markup.Escape(repoUrl)} ~/.clawsharp/skills/{Markup.Escape(skill)}");
        }
    }

    private static async Task GitHubApiDownloadAsync(string skill, string repo, string repoPath, string destDir, CancellationToken ct)
    {
        AnsiConsole.Markup($"  Installing {Markup.Escape(skill)}... ");
        try
        {
            Directory.CreateDirectory(destDir);
            await DownloadGitHubDirAsync(SharedHttpClient, repo, repoPath, destDir, ct);
            AnsiConsole.MarkupLine("[green]done[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]failed[/]");
            AnsiConsole.MarkupLine($"    {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine($"    Manual: https://github.com/{Markup.Escape(repo)}/tree/main/{Markup.Escape(repoPath)}");
        }
    }

    private static async Task DownloadGitHubDirAsync(HttpClient http, string repo, string path, string localDir, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/contents/{path}";
        var response = await http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(response);

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var type = entry.GetProperty("type").GetString()!;
            var local = Path.Combine(localDir, name);

            if (type == "file")
            {
                if (!entry.TryGetProperty("download_url", out var dlProp) ||
                    dlProp.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                var dlUrl = dlProp.GetString()!;
                var bytes = await http.GetByteArrayAsync(dlUrl, ct);
                // Redact hardcoded demo API key that ships in supermemory SKILL.md
                if (name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    var text = Encoding.UTF8.GetString(bytes);
                    text = RedactDemoKeys(text);
                    bytes = Encoding.UTF8.GetBytes(text);
                }

                await File.WriteAllBytesAsync(local, bytes, ct);
            }
            else if (type == "dir")
            {
                Directory.CreateDirectory(local);
                await DownloadGitHubDirAsync(http, repo, $"{path}/{name}", local, ct);
            }
        }
    }

    /// <summary>Replaces known public demo keys embedded in skill documentation.</summary>
    private static string RedactDemoKeys(string content)
    {
        const string supermemoryDemoKey =
            "sm_oiZHA2HcwT4tqSKmA7cCoK_opSRFViNFNxbYqjkjpVNfjSPqQWCNoOBAcxKZkKBfRVVrEQDVxLWHJPvepxqwEPe";
        return content.Replace(supermemoryDemoKey, "YOUR_SUPERMEMORY_API_KEY_HERE",
            StringComparison.Ordinal);
    }

    // ── Built-in skill content ──────────────────────────────────────────────────

    internal const string SkillVetterContent = """
                                               ---
                                               name: skill-vetter
                                               version: 1.0.0
                                               description: Security-first skill vetting for AI agents. Use before installing any skill from ClawdHub, GitHub, or other sources. Checks for red flags, permission scope, and suspicious patterns.
                                               ---

                                               # Skill Vetter

                                               Security-first vetting protocol for AI agent skills. **Never install a skill without vetting it first.**

                                               ## When to Use

                                               - Before installing any skill from ClawdHub
                                               - Before running skills from GitHub repos
                                               - When evaluating skills shared by other agents
                                               - Anytime you're asked to install unknown code

                                               ## Vetting Protocol

                                               ### Step 1: Source Check

                                               - Where did this skill come from?
                                               - Is the author known/reputable?
                                               - How many downloads/stars does it have?
                                               - When was it last updated?
                                               - Are there reviews from other agents?

                                               ### Step 2: Code Review (MANDATORY)

                                               Read ALL files in the skill. Check for these RED FLAGS:

                                               REJECT IMMEDIATELY IF YOU SEE:
                                               - curl/wget to unknown URLs
                                               - Sends data to external servers
                                               - Requests credentials/tokens/API keys
                                               - Reads ~/.ssh, ~/.aws, ~/.config without clear reason
                                               - Accesses MEMORY.md, USER.md, SOUL.md, IDENTITY.md
                                               - Uses base64 decode on anything
                                               - Uses eval() or exec() with external input
                                               - Modifies system files outside workspace
                                               - Installs packages without listing them
                                               - Network calls to IPs instead of domains
                                               - Obfuscated code (compressed, encoded, minified)
                                               - Requests elevated/sudo permissions
                                               - Accesses browser cookies/sessions
                                               - Touches credential files

                                               ### Step 3: Permission Scope

                                               - What files does it need to read?
                                               - What files does it need to write?
                                               - What commands does it run?
                                               - Does it need network access? To where?
                                               - Is the scope minimal for its stated purpose?

                                               ### Step 4: Risk Classification

                                               | Risk Level | Examples | Action |
                                               |------------|----------|--------|
                                               | LOW | Notes, weather, formatting | Basic review, install OK |
                                               | MEDIUM | File ops, browser, APIs | Full code review required |
                                               | HIGH | Credentials, trading, system | Human approval required |
                                               | EXTREME | Security configs, root access | Do NOT install |

                                               ## Trust Hierarchy

                                               1. Official OpenClaw skills - Lower scrutiny (still review)
                                               2. High-star repos (1000+) - Moderate scrutiny
                                               3. Known authors - Moderate scrutiny
                                               4. New/unknown sources - Maximum scrutiny
                                               5. Skills requesting credentials - Human approval always

                                               ## Remember

                                               - No skill is worth compromising security
                                               - When in doubt, don't install
                                               - Ask your human for high-risk decisions
                                               - Document what you vet for future reference
                                               """;

    internal const string DontHackMeContent = """
                                              ---
                                              name: dont-hack-me
                                              description: >-
                                                Security self-check for your agent config. Run a quick audit to catch
                                                dangerous misconfigurations -- exposed gateway, missing auth, open DM policy,
                                                weak tokens, loose file permissions. Auto-fix included.
                                                Invoke: "run a security check" or "check my security settings".
                                              author: "Ann Agent"
                                              homepage: https://github.com/peterann/dont-hack-me
                                              ---

                                              # dont-hack-me

                                              Security self-check skill for your agent config.
                                              Reads your agent config file and checks 7 items that cover the most
                                              common misconfigurations. Outputs a simple PASS / FAIL / WARN report.

                                              ## How to run

                                              Say any of:

                                              - "run a security check"
                                              - "check my security settings"
                                              - "audit my agent config"
                                              - "am I secure?"

                                              ## Checklist

                                              ### Step 0 -- Read the config

                                              Use the read tool to open ~/.clawsharp/config.json. Parse the JSON content.
                                              If the file does not exist or is unreadable, report an error and stop.

                                              ### Step 1 -- Gateway / Web Channel Bind

                                              - Check: if the web channel is enabled, is it bound to loopback only?
                                              - PASS if web is disabled or bound to localhost/127.0.0.1
                                              - FAIL if bound to 0.0.0.0 or any public interface

                                              ### Step 2 -- Auth Tokens Present

                                              - Check: any channel tokens present (telegram.token, discord.token, etc.)
                                              - PASS if tokens are set (non-empty strings)
                                              - WARN if a channel is enabled but token is empty or missing

                                              ### Step 3 -- Token Strength

                                              - Check: any auth tokens >= 32 characters
                                              - PASS if >= 32 chars
                                              - WARN if 16-31 chars
                                              - FAIL if < 16 chars

                                              ### Step 4 -- Allow Lists Configured

                                              - Check: telegram/matrix allowFrom fields
                                              - PASS if allowFrom is set with at least one entry
                                              - WARN if allowFrom is null (allow all)
                                              - FAIL if allowFrom is empty array (deny all)

                                              ### Step 5 -- Shell Tool Access

                                              - Check: tools.allowShell config
                                              - PASS if shell is disabled or restricted to CLI only
                                              - WARN if shell is enabled on non-CLI channels

                                              ### Step 6 -- File Permissions

                                              - Check: file mode of config.json
                                              - PASS if permissions are 600 or 400
                                              - WARN if permissions are 644 or 640
                                              - FAIL if world-writable (777, 666)

                                              ### Step 7 -- Plaintext Secrets Scan

                                              - Check: scan all string values for keys named password, secret,
                                                apiKey, api_key, privateKey with non-empty values
                                              - PASS if no such keys found
                                              - WARN if such keys exist -- suggest using environment variables
                                              """;

    internal const string QmdContent = """
                                       ---
                                       name: qmd
                                       description: "Local file indexing and search using BM25 + vector hybrid with reranking. Requires the qmd CLI (npm install -g from github.com/tobi/qmd). Use to search your workspace, docs, notes, or any local files."
                                       homepage: https://github.com/tobi/qmd
                                       ---

                                       # qmd

                                       Use qmd to index local files and search them with BM25, vector, or hybrid search.

                                       ## Setup (one-time)

                                       ```bash
                                       # Install qmd CLI
                                       npm install -g https://github.com/tobi/qmd

                                       # Add your workspace to an index
                                       qmd collection add ~/.clawsharp/workspace --name workspace --mask "**/*.md"
                                       qmd update
                                       ```

                                       ## Indexing

                                       - Add collection: qmd collection add /path --name docs --mask "**/*.md"
                                       - Update index:   qmd update
                                       - Status:         qmd status

                                       ## Search

                                       - BM25 (keyword): qmd search "query"
                                       - Vector (semantic): qmd vsearch "query"
                                       - Hybrid + rerank:   qmd query "query"
                                       - Get doc snippet:   qmd get docs/path.md:10 -l 40

                                       ## Notes

                                       - Embeddings/rerank use Ollama at OLLAMA_URL (default http://localhost:11434).
                                       - Index lives under ~/.cache/qmd by default.
                                       - MCP mode: qmd mcp (exposes search as MCP tools for agent use).
                                       - Works entirely offline -- no external API calls.
                                       """;
}