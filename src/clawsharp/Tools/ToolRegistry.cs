using System.Collections.Concurrent;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Cost;
using Clawsharp.Goals;
using Clawsharp.Memory;
using Clawsharp.Providers;
using Clawsharp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Features;
using Clawsharp.Tools.Browser;
using Clawsharp.Tools.Files;
using Clawsharp.Tools.Memory;
using Clawsharp.Tools.Ops;
using Clawsharp.Tools.Web;

namespace Clawsharp.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private static readonly AsyncLocal<string?> _currentChannelName = new();

    private static readonly AsyncLocal<string?> _currentSessionId = new();

    private static readonly AsyncLocal<int> _currentSpawnDepth = new();

    /// <summary>Current channel name for the executing async flow. Read by tools via AsyncLocal.</summary>
    public static string? CurrentChannelName => _currentChannelName.Value;

    /// <summary>Current session ID for the executing async flow. Read by tools via AsyncLocal.</summary>
    public static string? CurrentSessionId => _currentSessionId.Value;

    /// <summary>Current spawn depth for the executing async flow. Read by tools via AsyncLocal.</summary>
    public static int CurrentSpawnDepth => _currentSpawnDepth.Value;

    private readonly ConcurrentDictionary<string, JsonDocument?> _schemaCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Tool> _tools;

    private readonly int _maxToolOutputChars;

    private readonly Dictionary<string, ToolFilterGroup>? _filterGroups;

    private readonly ToolSensitivity _maxNonCliSensitivity;

    public ToolRegistry(
        IOptions<AppConfig> configOptions,
        IOptions<AgentDefaults> defaultsOptions,
        IMemory memory,
        CronService cronService,
        IProvider provider,
        IHttpClientFactory httpFactory,
        AuditLogger auditLogger,
        SandboxProbe sandboxProbe,
        BrowserSessionManager browserSessions,
        GoalStorage goalStorage,
        RateLimiter rateLimiter,
        CostTracker costTracker,
        ILoggerFactory loggerFactory)
    {
        var config = configOptions.Value;
        var defaults = defaultsOptions.Value;
        _maxToolOutputChars = Math.Max(1024, config.Tools.MaxToolOutputChars);
        _filterGroups = config.Tools.FilterGroups;
        _maxNonCliSensitivity = ParseSensitivity(config.Security?.MaxNonCliToolSensitivity ?? "high");
        var workspace = ConfigLoader.ExpandHome(config.Tools.Workspace);
        var list = new List<Tool>
        {
            new ShellTool(workspace, config.Tools.RequireShellApproval,
                config.Tools.EnableShellDenyPatterns, config.Tools.CustomShellDenyPatterns, auditLogger, sandboxProbe,
                config.Security?.RequireApprovalPatterns, config.Security?.AutoApprovePatterns),
            new FileReadTool(workspace, auditLogger),
            new FileWriteTool(workspace, auditLogger),
            new FileEditTool(workspace, auditLogger),
            new FileListTool(workspace),
            new FileSearchTool(workspace),
            new WebFetchTool(httpFactory, auditLogger, config.Security?.AllowedExternalDomains),
            new WebSearchTool(httpFactory, config.Tools, auditLogger),
            new MemoryReadTool(memory),
            new MemoryWriteTool(memory),
            new MemorySearchTool(memory),
            new HistoryAppendTool(memory),
            new CronTool(cronService),
            new SpawnTool(provider, this, memory, defaults, rateLimiter, costTracker, loggerFactory.CreateLogger<SpawnTool>()),
            new GitTool(workspace, auditLogger),
            new ScreenshotTool(workspace, auditLogger),
            new DocumentReadTool(workspace, auditLogger),
            new BrowserTool(browserSessions, configOptions, auditLogger, loggerFactory.CreateLogger<BrowserTool>()),
            new PinchTabTool(new PinchTabSessionManager(), configOptions, httpFactory, auditLogger,
                loggerFactory.CreateLogger<PinchTabTool>()),
            new GoalTool(goalStorage, loggerFactory.CreateLogger<GoalTool>()),
            new SendFileTool(workspace, auditLogger),
        };

        _tools = new ConcurrentDictionary<string, Tool>(
            list.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Registers a tool dynamically (e.g. from an MCP server).</summary>
    public void Register(Tool tool) => _tools[tool.Name] = tool;

    /// <summary>Sets per-request channel context via AsyncLocal so each async call chain
    /// gets its own isolated value, preventing cross-channel corruption on shared singletons.</summary>
    public void SetChannelContext(string channelName, int spawnDepth = 0, string? sessionId = null)
    {
        _currentChannelName.Value = channelName;
        _currentSpawnDepth.Value = spawnDepth;
        _currentSessionId.Value = sessionId;
    }

    public IReadOnlyList<Core.ToolDefinition> GetDefinitions()
    {
        return _tools.Values.Select(t => t.ToDefinition()).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<Core.ToolDefinition> GetFilteredDefinitions(string? messageText)
    {
        if (_filterGroups is null || _filterGroups.Count == 0)
        {
            return GetDefinitions();
        }

        return _tools.Values
                     .Where(t => ShouldIncludeTool(t.Name, messageText))
                     .Select(t => t.ToDefinition())
                     .ToList();
    }

    /// <summary>
    /// Evaluates filter groups to decide whether a tool should be included.
    /// A tool not matched by any group pattern is always included (default: include).
    /// If matched by any "always" group, include. If matched only by "dynamic" groups,
    /// include only when the message text contains at least one keyword from those groups.
    /// </summary>
    private bool ShouldIncludeTool(string toolName, string? messageText)
    {
        if (_filterGroups is null || _filterGroups.Count == 0)
        {
            return true;
        }

        var matchedByAny = false;
        var includedByAlways = false;
        List<string>? dynamicKeywords = null;

        foreach (var group in _filterGroups.Values)
        {
            if (group.ToolPatterns is null || group.ToolPatterns.Count == 0)
            {
                continue;
            }

            var matches = false;
            foreach (var pattern in group.ToolPatterns)
            {
                if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, toolName, ignoreCase: true))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            matchedByAny = true;

            if (string.Equals(group.Mode, "always", StringComparison.OrdinalIgnoreCase))
            {
                includedByAlways = true;
            }
            else if (string.Equals(group.Mode, "dynamic", StringComparison.OrdinalIgnoreCase)
                     && group.Keywords is { Count: > 0 })
            {
                dynamicKeywords ??= [];
                dynamicKeywords.AddRange(group.Keywords);
            }
        }

        // Not matched by any filter group — default include.
        if (!matchedByAny)
        {
            return true;
        }

        // Matched by at least one "always" group — include.
        if (includedByAlways)
        {
            return true;
        }

        // Only matched by "dynamic" groups — check keywords against message.
        if (dynamicKeywords is not null && !string.IsNullOrEmpty(messageText))
        {
            foreach (var kw in dynamicKeywords)
            {
                if (messageText.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ToolSensitivity ParseSensitivity(string value) => value.ToLowerInvariant() switch
    {
        "low" => ToolSensitivity.Low,
        "medium" => ToolSensitivity.Medium,
        "high" => ToolSensitivity.High,
        "critical" or "unrestricted" => ToolSensitivity.Critical,
        _ => ToolSensitivity.High,
    };

    public async Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return $"Error: unknown tool '{name}'.";
        }

        // Channel-based sensitivity enforcement: block tools above the configured
        // threshold on non-CLI channels to prevent prompt injection from triggering
        // high-impact operations via external messaging channels.
        var channel = CurrentChannelName;
        if (channel is not (null or "cli") && tool.Sensitivity > _maxNonCliSensitivity)
        {
            return $"[security] Tool '{name}' (sensitivity: {tool.Sensitivity}) is not available on " +
                   $"the {channel} channel. Maximum allowed: {_maxNonCliSensitivity}. " +
                   "Use the CLI channel for this operation, or adjust security.maxNonCliToolSensitivity in config.";
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(argumentsJson) ? "{}" : argumentsJson);

            // Validate arguments against the tool's declared JSON schema.
            var validationError = ValidateArguments(tool, doc.RootElement);
            if (validationError is not null)
            {
                return validationError;
            }

            var result = await tool.ExecuteAsync(doc.RootElement, ct);

            // Global safety-net truncation — individual tools may have their own lower caps.
            if (result.Length > _maxToolOutputChars)
            {
                result = string.Concat(
                    result.AsSpan(0, _maxToolOutputChars),
                    $"\n... (truncated at {_maxToolOutputChars:N0} of {result.Length:N0} chars)");
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"Tool error: {ex.Message}";
        }
    }

    /// <summary>
    /// Validates tool call arguments against the tool's parameter schema.
    /// Returns an error string if invalid, or null if valid.
    /// Schemas are parsed and cached on first use per tool.
    /// </summary>
    private string? ValidateArguments(Tool tool, JsonElement arguments)
    {
        var schemaDoc = _schemaCache.GetOrAdd(tool.Name, _ =>
        {
            try
            {
                return JsonDocument.Parse(tool.ParametersSchemaJson);
            }
            catch
            {
                return null;
            }
        });

        if (schemaDoc is null)
        {
            return null; // Unparseable schema — skip validation rather than blocking execution.
        }

        var error = ToolValidator.Validate(schemaDoc.RootElement, arguments);
        if (error is not null)
        {
            return $"Tool input validation error for '{tool.Name}': {error} "
                   + "Please fix the arguments and try again.";
        }

        return null;
    }
}