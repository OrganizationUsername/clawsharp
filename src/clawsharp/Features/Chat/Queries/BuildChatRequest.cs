using System.Text;
using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Goals;
using Clawsharp.Security;
using Clawsharp.Tools;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Features.Chat.Queries;

/// <summary>
/// Assembles the full LLM request context: loads workspace context (SYSTEM.md),
/// active goals, filtered tool definitions, and builds the split system prompt
/// with caching flags. Encapsulates the logic previously in
/// <c>AgentLoop.BuildRequestContextAsync</c>, <c>LoadWorkspaceContextAsync</c>,
/// and <c>BuildGoalsContextAsync</c>.
/// </summary>
[Handler]
public static partial class BuildChatRequest
{
    private const int MaxGoalsContextChars = 500;

    /// <summary>Logger category for DI resolution (static types cannot be used as type arguments).</summary>
    public sealed class Log;

    public sealed record Query(
        InboundMessage Inbound,
        /// <summary>Pre-fetched memory context from GetMemoryContext handler.</summary>
        string? MemoryContext);

    public sealed record Result(
        string StaticPrompt,
        string? DynamicPrompt,
        string SystemPrompt,
        IReadOnlyList<ToolDefinition> ToolDefinitions,
        bool CachingEnabled,
        bool CacheToolDefs,
        bool ContextWindowEnabled);

    private static async ValueTask<Result> HandleAsync(
        Query query,
        IToolRegistry toolRegistry,
        GoalStorage goalStorage,
        IOptions<AgentDefaults> defaults,
        IOptions<AppConfig> appConfig,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        var inbound = query.Inbound;
        var cfg = defaults.Value;
        var workspacePath = ConfigLoader.ExpandHome(appConfig.Value.Tools.Workspace);

        // Load workspace context (SYSTEM.md) — best-effort, never breaks the pipeline.
        var workspaceContext = await LoadWorkspaceContextAsync(workspacePath, logger, ct);

        // Build goals context — best-effort, never breaks the pipeline.
        var goalsContext = await BuildGoalsContextAsync(goalStorage, ct);

        // Set tool context and get filtered definitions for this message.
        toolRegistry.SetChannelContext(inbound.Channel.Value, inbound.SpawnDepth,
            sessionId: $"{inbound.Channel.Value}:{inbound.SenderId}");
        var toolDefs = toolRegistry.GetFilteredDefinitions(inbound.Text);

        // Caching flags — enabled by default when config is absent.
        var cachingEnabled = cfg.Caching?.Enabled ?? true;
        var cacheToolDefs = cachingEnabled && (cfg.Caching?.CacheToolDefinitions ?? true);

        // Build split system prompt (static part = cacheable, dynamic part = per-request).
        var (staticPrompt, dynamicPrompt) = SystemPromptBuilder.BuildSplit(
            query.MemoryContext,
            workspaceContext,
            channelName: inbound.Channel.Value,
            enabledTools: toolDefs.Select(t => t.Name).ToList(),
            activeGoalsContext: goalsContext);

        var systemPrompt = string.IsNullOrEmpty(dynamicPrompt)
            ? staticPrompt
            : staticPrompt + "\n\n" + dynamicPrompt;

        var cwEnabled = cfg.ContextWindow is { Enabled: true };

        return new Result(
            StaticPrompt: staticPrompt,
            DynamicPrompt: dynamicPrompt,
            SystemPrompt: systemPrompt,
            ToolDefinitions: toolDefs,
            CachingEnabled: cachingEnabled,
            CacheToolDefs: cacheToolDefs,
            ContextWindowEnabled: cwEnabled);
    }

    /// <summary>
    /// Reads the SYSTEM.md file from the workspace directory if it exists.
    /// Returns null on any I/O failure — workspace context is strictly best-effort.
    /// </summary>
    private static async Task<string?> LoadWorkspaceContextAsync(
        string workspacePath,
        ILogger logger,
        CancellationToken ct)
    {
        var systemMdPath = Path.Combine(workspacePath, "SYSTEM.md");
        if (!File.Exists(systemMdPath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(systemMdPath, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read workspace SYSTEM.md at {Path}", systemMdPath);
            return null;
        }
    }

    /// <summary>
    /// Builds a brief summary of active goals for system prompt injection.
    /// Returns null when no active goals exist or on any failure.
    /// Capped at ~500 characters to avoid bloating every prompt.
    /// </summary>
    private static async Task<string?> BuildGoalsContextAsync(
        GoalStorage goalStorage,
        CancellationToken ct)
    {
        try
        {
            var goals = await goalStorage.LoadAsync(ct);
            var active = goals.Where(g => g.Status == GoalStatus.Active).ToList();
            if (active.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var g in active)
            {
                var doneCount = g.Steps.Count(s => s.Done);
                var stepInfo = g.Steps.Count > 0 ? $" ({doneCount}/{g.Steps.Count} steps done)" : "";
                sb.AppendLine($"- [{g.Id}] {PromptGuard.EscapeDelimiterContent(g.Title)}{stepInfo}");

                // Cap at ~500 chars to avoid bloating every prompt.
                if (sb.Length > MaxGoalsContextChars)
                {
                    sb.AppendLine("  ...(more goals truncated)");
                    break;
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch
        {
            // Never break the prompt builder for goal loading failures.
            return null;
        }
    }
}