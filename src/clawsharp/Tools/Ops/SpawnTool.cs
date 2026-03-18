using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Cost;
using Clawsharp.Memory;
using Clawsharp.Providers;
using Microsoft.Extensions.Logging;
using Clawsharp.Config.Agent;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Tools.Ops;

/// <summary>
///     Spawns an isolated sub-agent that processes a task using its own session and tool loop.
///     Depth-limited to 2 levels to prevent runaway nesting.
/// </summary>
/// <remarks>
/// SECURITY: The child agent inherits the parent's full tool set by default, including
/// potentially dangerous tools (shell, file_write, file_edit, etc.). This is by design —
/// the spawn_agent tool is itself gated behind the parent's tool permissions, and the child
/// operates within the same security context (PathGuard, ShellGuard, SsrfGuard, AuditLogger).
/// Callers can optionally provide a "restricted_tools" parameter to limit which tools the
/// child agent can use.
///
/// HIGH-04 fix: Before spawning, the tool enforces rate limiting (shared with the parent
/// session's quota via the same <see cref="RateLimiter"/> key) and cost budget checks
/// (via <see cref="CostTracker"/>). If either check fails, the spawn is denied immediately.
/// </remarks>
public sealed partial class SpawnTool(
    IProvider provider,
    IToolRegistry tools,
    IMemory memory,
    AgentDefaults defaults,
    RateLimiter rateLimiter,
    CostTracker costTracker,
    ILogger<SpawnTool> logger)
    : Tool
{
    private const int MaxSpawnDepth = 2;

    private static readonly TimeSpan SpawnTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Current spawn depth of the parent that owns this tool instance.
    ///     Reads from the per-async-flow AsyncLocal in ToolRegistry.
    /// </summary>
    public int CurrentSpawnDepth => ToolRegistry.CurrentSpawnDepth;

    public override string Name => "spawn_agent";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Critical;

    public override string Description =>
        "Spawn an isolated sub-agent to handle a specific task. The sub-agent runs independently and returns its result. Max 2 levels of nesting.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "task": {
                                                         "type": "string",
                                                         "description": "The task or question for the sub-agent to work on"
                                                       },
                                                       "agent_name": {
                                                         "type": "string",
                                                         "description": "Optional name for this sub-agent (for logging)"
                                                       },
                                                       "restricted_tools": {
                                                         "type": "array",
                                                         "items": { "type": "string" },
                                                         "description": "Optional allowlist of tool names the child agent may use. If omitted, inherits all parent tools."
                                                       }
                                                     },
                                                     "required": ["task"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        if (!defaults.AllowSubagents)
        {
            return "Error: sub-agent spawning is disabled. Set defaults.allowSubagents=true in config to enable.";
        }

        if (CurrentSpawnDepth >= MaxSpawnDepth)
        {
            return $"Error: maximum sub-agent depth ({MaxSpawnDepth}) reached. Cannot spawn further.";
        }

        // HIGH-04: Enforce rate limiting — child agents share the parent's rate limit key
        // so that spawned sub-agents count against the same per-session quota.
        var rateLimitKey = ToolRegistry.CurrentSessionId ?? "spawn";
        if (!rateLimiter.TryAcquire(rateLimitKey))
        {
            LogSpawnRateLimited(logger, rateLimitKey);
            return "Error: rate limit exceeded. The sub-agent spawn was denied because the current session has exceeded its request quota.";
        }

        // HIGH-04: Enforce cost budget — reject spawn if budget is already exhausted.
        // Pass 0 for estimated cost; this still blocks when the budget is already over the limit.
        var budgetCheck = await costTracker.CheckBudgetAsync(0, ct).ConfigureAwait(false);
        if (budgetCheck.Status == BudgetStatus.Exceeded)
        {
            LogSpawnBudgetExceeded(logger, budgetCheck.Message ?? "budget exceeded");
            return $"Error: cost budget exceeded. The sub-agent spawn was denied. {budgetCheck.Message}";
        }

        var task = arguments.TryGetProperty("task", out var taskProp) ? taskProp.GetString() ?? "" : "";
        var agentName = arguments.TryGetProperty("agent_name", out var nameProp) ? nameProp.GetString() : null;

        // Parse optional tool restriction allowlist
        HashSet<string>? restrictedTools = null;
        if (arguments.TryGetProperty("restricted_tools", out var rtProp) && rtProp.ValueKind == JsonValueKind.Array)
        {
            restrictedTools = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in rtProp.EnumerateArray())
            {
                if (item.GetString() is { Length: > 0 } toolName)
                {
                    restrictedTools.Add(toolName);
                }
            }

            if (restrictedTools.Count == 0)
            {
                restrictedTools = null; // Empty array = no restriction
            }
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return "Error: 'task' parameter is required and must not be empty.";
        }

        var spawnId = $"spawn:{Guid.NewGuid():N}";
        var displayName = agentName ?? spawnId;
        LogSpawning(logger, displayName, CurrentSpawnDepth + 1);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SpawnTimeout);

        try
        {
            var result = await RunChildLoopAsync(task, spawnId, restrictedTools, cts.Token);
            LogSpawnCompleted(logger, displayName, result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogSpawnTimedOut(logger, displayName);
            return $"Error: sub-agent '{displayName}' timed out after {SpawnTimeout.TotalSeconds}s.";
        }
        catch (Exception ex)
        {
            LogSpawnError(logger, ex, displayName);
            return $"Error: sub-agent '{displayName}' failed.";
        }
    }

    /// <summary>
    ///     Runs a minimal non-streaming tool loop as a child agent.
    ///     Uses an in-memory session (not persisted) and the same provider/tools as the parent.
    /// </summary>
    private async Task<string> RunChildLoopAsync(
        string task, string sessionId, HashSet<string>? restrictedTools, CancellationToken ct)
    {
        var memoryCtx = await memory.GetContextAsync(ct).ConfigureAwait(false);
        var allToolDefs = tools.GetDefinitions();

        // If restricted_tools is provided, filter to only those tools
        IReadOnlyList<ToolDefinition> toolDefs;
        if (restrictedTools is not null)
        {
            toolDefs = allToolDefs.Where(t => restrictedTools.Contains(t.Name)).ToList();
        }
        else
        {
            toolDefs = allToolDefs;
        }

        var cachingEnabled = defaults.Caching?.Enabled ?? true;
        var cacheToolDefs = cachingEnabled && (defaults.Caching?.CacheToolDefinitions ?? true);

        var (staticPrompt, dynamicPrompt) = SystemPromptBuilder.BuildSplit(
            memoryCtx,
            workspaceContext: null,
            channelName: "spawn",
            enabledTools: toolDefs.Select(t => t.Name).ToList());

        string systemPrompt;
        if (string.IsNullOrEmpty(dynamicPrompt))
        {
            systemPrompt = staticPrompt;
        }
        else
        {
            systemPrompt = staticPrompt + "\n\n" + dynamicPrompt;
        }

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, systemPrompt),
            new(MessageRole.User, task)
        };

        IReadOnlyList<ToolDefinition>? spawnTools = null;
        if (toolDefs.Count > 0)
        {
            spawnTools = toolDefs;
        }

        string? spawnStaticPrompt = null;
        string? spawnDynamicPrompt = null;
        if (cachingEnabled)
        {
            spawnStaticPrompt = staticPrompt;
            spawnDynamicPrompt = dynamicPrompt;
        }

        var request = new ChatRequest(
            Model: defaults.Model,
            Messages: messages,
            Tools: spawnTools,
            Temperature: defaults.Temperature,
            SystemStaticPart: spawnStaticPrompt,
            SystemDynamicPart: spawnDynamicPrompt,
            CacheToolDefinitions: cacheToolDefs
        );

        // Cap iterations for the child — use the same config limit.
        for (var iteration = 0; iteration < defaults.MaxToolIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            ChatResponse response;
            try
            {
                response = await provider.ChatAsync(request, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sub-agent provider request failed");
                return "Error: sub-agent provider request failed.";
            }

            if (response.ToolCalls?.Count > 0)
            {
                messages.Add(new ChatMessage(MessageRole.Assistant, response.Content, ToolCalls: response.ToolCalls));
                foreach (var tc in response.ToolCalls)
                {
                    // Propagate spawn depth to nested SpawnTool calls
                    SetChildSpawnDepth(CurrentSpawnDepth + 1);
                    var result = await tools.ExecuteAsync(tc.Name, tc.ArgumentsJson, ct);
                    messages.Add(new ChatMessage(MessageRole.Tool, result, ToolCallId: tc.Id, Name: tc.Name));
                }

                request = request with { Messages = messages };
                continue;
            }

            // Final response — return the text
            var finalReply = response.Content ?? "(no response)";
            if (response.ReasoningContent is { Length: > 0 } thinking)
            {
                finalReply = $"<thinking>\n{thinking}\n</thinking>\n\n{finalReply}";
            }

            return finalReply;
        }

        return "(sub-agent reached maximum tool iterations without a final response)";
    }

    /// <summary>
    ///     Propagates spawn depth to the AsyncLocal context so nested tool calls
    ///     see the correct depth via <see cref="ToolRegistry.CurrentSpawnDepth"/>.
    /// </summary>
    private void SetChildSpawnDepth(int depth)
    {
        tools.SetChannelContext(ToolRegistry.CurrentChannelName ?? ChannelName.Cli, depth, ToolRegistry.CurrentSessionId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Spawning sub-agent '{AgentName}' at depth {Depth}")]
    private static partial void LogSpawning(ILogger logger, string agentName, int depth);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Sub-agent '{AgentName}' completed ({ResultLength} chars)")]
    private static partial void LogSpawnCompleted(ILogger logger, string agentName, int resultLength);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Sub-agent '{AgentName}' timed out")]
    private static partial void LogSpawnTimedOut(ILogger logger, string agentName);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Sub-agent '{AgentName}' error")]
    private static partial void LogSpawnError(ILogger logger, Exception exception, string agentName);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Sub-agent spawn denied: rate limit exceeded for '{RateLimitKey}'")]
    private static partial void LogSpawnRateLimited(ILogger logger, string rateLimitKey);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Sub-agent spawn denied: {BudgetMessage}")]
    private static partial void LogSpawnBudgetExceeded(ILogger logger, string budgetMessage);
}