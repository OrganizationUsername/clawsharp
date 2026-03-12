using System.Text;
using Clawsharp.Features.Cost.Queries;
using Clawsharp.Features.Session.Commands;
using Clawsharp.Goals;
using Clawsharp.Config.Features;
using Clawsharp.Core.Sessions;

namespace Clawsharp.Core.Pipeline;

public sealed partial class AgentLoop
{
    private async Task<string?> HandleSlashCommandAsync(
        SlashCommandResult cmd,
        Session session,
        CancellationToken ct)
    {
        switch (cmd)
        {
            case SlashCommandResult.ClearSession:
                await _handlers.ClearSession.HandleAsync(new ClearSession.Command(session), ct);
                return "Session cleared.";

            case SlashCommandResult.SendStatus:
                var factCount = 0;
                try
                {
                    var ctx = await _memory.GetContextAsync(ct);
                    factCount = ctx?.Split('\n').Length ?? 0;
                }
                catch
                {
                    /* best-effort */
                }

                return $"Model: {_defaults.Model}\n" +
                       $"Messages: {session.Messages.Count}\n" +
                       $"Total turns: {session.TotalMessageCount / 2}\n" +
                       $"Input tokens: {session.TotalInputTokens:N0}\n" +
                       $"Output tokens: {session.TotalOutputTokens:N0}\n" +
                       $"Memory lines: {factCount}\n" +
                       $"Thinking: {(session.ShowThinking ? "on" : "off")}";

            case SlashCommandResult.TriggerCompaction:
                if (_defaults.ContextWindow is not { Enabled: true })
                {
                    return "Context compaction is not enabled (set agents.defaults.contextWindow.enabled: true).";
                }

                var compConfig = _defaults.Compaction ?? new CompactionConfig();
                var msgs = new List<ChatMessage>(session.Messages);
                var compacted = await _compactionService.CompactAsync(
                    msgs, _provider, _defaults.Model,
                    compConfig.KeepRecent, compConfig.MaxSummaryChars, compConfig.MaxSourceChars, ct);
                session.Messages.Clear();
                session.Messages.AddRange(compacted.Where(m => m.Role != MessageRole.System));
                await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct);
                return $"Compacted: {msgs.Count} -> {session.Messages.Count} messages.";

            case SlashCommandResult.ShowUsage:
                if (_appConfig.Cost is not { Enabled: true })
                {
                    return "Cost tracking is not enabled.\nSet cost.enabled: true in config to enable it.";
                }

                var summary = await _handlers.GetCostSummary.HandleAsync(new GetCostSummary.Query(session.Id), ct);
                var usageSb = new StringBuilder();
                usageSb.AppendLine($"Usage (today):        ${summary.Daily:F4}");
                usageSb.AppendLine($"Usage (this month):   ${summary.Monthly:F4}");
                usageSb.AppendLine($"Usage (this session): ${summary.Session:F4}");

                if (summary.SessionSavings > 0)
                {
                    usageSb.AppendLine($"Cache saved (session): ${summary.SessionSavings:F4}");
                }

                if (summary.DailySavings > 0)
                {
                    usageSb.AppendLine($"Cache saved (today):  ${summary.DailySavings:F4}");
                }

                var costCfg = _appConfig.Cost!;
                if (costCfg.DailyLimitUsd > 0)
                {
                    usageSb.AppendLine(
                        $"Daily limit:  ${costCfg.DailyLimitUsd:F2} ({summary.Daily / costCfg.DailyLimitUsd * 100:F0}% used)");
                }

                if (costCfg.MonthlyLimitUsd > 0)
                {
                    usageSb.AppendLine(
                        $"Monthly limit:${costCfg.MonthlyLimitUsd:F2} ({summary.Monthly / costCfg.MonthlyLimitUsd * 100:F0}% used)");
                }

                return usageSb.ToString().TrimEnd();

            case SlashCommandResult.ThinkOn:
                session.ShowThinking = true;
                await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct);
                return "Thinking mode on. Reasoning blocks will be shown in replies.";

            case SlashCommandResult.ThinkOff:
                session.ShowThinking = false;
                await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct);
                return "Thinking mode off.";

            case SlashCommandResult.ThinkToggle:
                session.ShowThinking = !session.ShowThinking;
                await _handlers.SaveSession.HandleAsync(new SaveSession.Command(session), ct);
                return $"Thinking mode {(session.ShowThinking ? "on" : "off")}.";

            case SlashCommandResult.ShowGoals:
                return await HandleGoalsCommandAsync(null, ct);

            case SlashCommandResult.ClearGoals:
                return await HandleGoalsCommandAsync("clear", ct);

            default:
                return null;
        }
    }

    // (BuildGoalsContextAsync -> now in BuildChatRequest handler)

    private async Task<string> HandleGoalsCommandAsync(string? subcommand, CancellationToken ct)
    {
        if (string.Equals(subcommand, "clear", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var goals = await _goalStorage.LoadAsync(ct);
                var cleared = 0;
                foreach (var g in goals.Where(g => g.Status == GoalStatus.Active || g.Status == GoalStatus.Paused))
                {
                    g.Status = GoalStatus.Deleted;
                    g.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    cleared++;
                }

                await _goalStorage.SaveAsync(goals, ct);
                return cleared > 0 ? $"Cleared {cleared} goal(s)." : "No active or paused goals to clear.";
            }
            catch (Exception ex)
            {
                return $"Failed to clear goals: {ex.Message}";
            }
        }

        // Default: list active goals
        try
        {
            var goals = await _goalStorage.LoadAsync(ct);
            var active = goals.Where(g => g.Status == GoalStatus.Active || g.Status == GoalStatus.Paused).ToList();
            if (active.Count == 0)
            {
                return "No active goals.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Goals:");
            foreach (var g in active)
            {
                var doneCount = g.Steps.Count(s => s.Done);
                var stepInfo = g.Steps.Count > 0 ? $" [{doneCount}/{g.Steps.Count} steps]" : "";
                sb.AppendLine($"  [{g.Id}] {g.Title}{stepInfo} ({g.Status})");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Failed to load goals: {ex.Message}";
        }
    }
}