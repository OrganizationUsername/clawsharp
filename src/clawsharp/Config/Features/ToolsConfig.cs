using Clawsharp.Config.Search;
namespace Clawsharp.Config.Features;

/// <summary>Tool configuration section.</summary>
public sealed class ToolsConfig
{
    /// <summary>Workspace directory for file tools.</summary>
    public string Workspace { get; init; } = "~/.clawsharp/workspace";

    /// <summary>Brave Search API configuration.</summary>
    public BraveConfig? Brave { get; init; }

    /// <summary>SearXNG search engine configuration.</summary>
    public SearxngConfig? Searxng { get; init; }

    /// <summary>Exa search API configuration.</summary>
    public ExaConfig? Exa { get; init; }

    /// <summary>Tavily search API configuration.</summary>
    public TavilyConfig? Tavily { get; init; }

    /// <summary>Jina search API configuration.</summary>
    public JinaConfig? Jina { get; init; }

    /// <summary>Firecrawl search API configuration.</summary>
    public FirecrawlConfig? Firecrawl { get; init; }

    /// <summary>Perplexity AI search API configuration.</summary>
    public PerplexityConfig? Perplexity { get; init; }

    /// <summary>GLM/Zhipu AI search API configuration (Chinese web search).</summary>
    public GlmConfig? Glm { get; init; }

    /// <summary>Whether shell commands require interactive approval.</summary>
    public bool RequireShellApproval { get; init; } = false;

    /// <summary>Whether to enable the shell command deny-list patterns (default true).</summary>
    public bool EnableShellDenyPatterns { get; init; } = true;

    /// <summary>Additional regex deny patterns for shell commands (applied after built-in patterns).</summary>
    public List<string>? CustomShellDenyPatterns { get; init; }

    /// <summary>
    /// Shell sandbox backend. Options: "auto" (default), "bubblewrap"/"bwrap", "firejail", "docker", "none".
    /// "auto" tries Bubblewrap, Firejail, Docker (in order) on Linux; Docker-only on other platforms; falls back to None.
    /// </summary>
    public string Sandbox { get; init; } = "auto";

    /// <summary>Docker image for the Docker sandbox backend. Default: "alpine:latest".</summary>
    public string DockerImage { get; init; } = "alpine:latest";

    /// <summary>
    /// Maximum characters returned from any single tool call (global safety net).
    /// Individual tools may have their own lower limits. Default: 102400 (~100 KB).
    /// </summary>
    public int MaxToolOutputChars { get; init; } = 102_400;

    /// <summary>
    /// Tool filter groups for dynamic tool inclusion. Groups map a set of tool name
    /// patterns to a mode ("always" or "dynamic") to reduce token usage by omitting
    /// rarely-used tool definitions from LLM requests.
    /// </summary>
    public Dictionary<string, ToolFilterGroup>? FilterGroups { get; init; }

    /// <summary>Browser automation (Playwright/Chromium) configuration.</summary>
    public BrowserConfig Browser { get; init; } = new();

    /// <summary>PinchTab HTTP browser server configuration.</summary>
    public PinchTabConfig PinchTab { get; init; } = new();
}

/// <summary>Configuration for the PinchTab HTTP browser automation server.</summary>
public sealed class PinchTabConfig
{
    /// <summary>Whether the PinchTab tool is enabled. Default: false (requires running PinchTab server).</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Base URL of the PinchTab server. Default: http://localhost:9867.</summary>
    public string BaseUrl { get; init; } = "http://localhost:9867";

    /// <summary>Optional Bearer token (BRIDGE_TOKEN) for authenticated PinchTab servers.</summary>
    public string? Token { get; init; }

    /// <summary>Whether JavaScript evaluation is enabled. Default: false (security-sensitive).</summary>
    public bool EvaluateEnabled { get; init; } = false;

    /// <summary>Optional domain allowlist. Null = all public domains allowed (SSRF guard still applies).</summary>
    public string[]? AllowedDomains { get; init; }
}

/// <summary>Configuration for the Playwright-based browser automation tool.</summary>
public sealed class BrowserConfig
{
    /// <summary>Whether the browser tool is enabled. Default: true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether JavaScript evaluation is enabled. Default: false (security-sensitive).</summary>
    public bool EvaluateEnabled { get; init; } = false;

    /// <summary>
    /// Optional domain allowlist. When set, only these domains can be navigated to.
    /// Null means all public domains are allowed (SSRF guard still applies).
    /// </summary>
    public string[]? AllowedDomains { get; init; }

    /// <summary>Directory for persisting browser session state. Default: ~/.clawsharp/sessions.</summary>
    public string SessionsDir { get; init; } = "~/.clawsharp/sessions";

    /// <summary>Whether to launch Chromium in headless mode. Default: true.</summary>
    public bool Headless { get; init; } = true;
}