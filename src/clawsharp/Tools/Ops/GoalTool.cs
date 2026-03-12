using System.Text;
using System.Text.Json;
using Clawsharp.Goals;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Tools.Ops;

public sealed partial class GoalTool : Tool
{
    private readonly GoalStorage _storage;

    private readonly ILogger<GoalTool> _logger;

    public GoalTool(GoalStorage storage, ILogger<GoalTool> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public override string Name => "goal";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description =>
        "Track goals and multi-step tasks. Actions: create, list, update_step, complete, pause, resume, delete.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "action": {
                                                         "type": "string",
                                                         "enum": ["create", "list", "update_step", "complete", "pause", "resume", "delete"],
                                                         "description": "Action to perform"
                                                       },
                                                       "title": {
                                                         "type": "string",
                                                         "description": "Goal title (required for create)"
                                                       },
                                                       "description": {
                                                         "type": "string",
                                                         "description": "Optional goal description (for create)"
                                                       },
                                                       "steps": {
                                                         "type": "array",
                                                         "items": { "type": "string" },
                                                         "description": "Initial step descriptions (for create)"
                                                       },
                                                       "id": {
                                                         "type": "string",
                                                         "description": "Goal ID (required for update_step, complete, pause, resume, delete)"
                                                       },
                                                       "step_index": {
                                                         "type": "integer",
                                                         "description": "Zero-based step index (required for update_step)"
                                                       },
                                                       "done": {
                                                         "type": "boolean",
                                                         "description": "Whether the step is done (required for update_step)"
                                                       },
                                                       "status": {
                                                         "type": "string",
                                                         "enum": ["active", "paused", "all"],
                                                         "description": "Filter for list action (default: active)"
                                                       }
                                                     },
                                                     "required": ["action"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var action = arguments.TryGetProperty("action", out var a) ? a.GetString() : null;

        return action switch
        {
            "create" => await CreateAsync(arguments, ct),
            "list" => await ListAsync(arguments, ct),
            "update_step" => await UpdateStepAsync(arguments, ct),
            "complete" => await SetStatusAsync(arguments, GoalStatus.Completed, ct),
            "pause" => await SetStatusAsync(arguments, GoalStatus.Paused, ct),
            "resume" => await SetStatusAsync(arguments, GoalStatus.Active, ct),
            "delete" => await SetStatusAsync(arguments, GoalStatus.Deleted, ct),
            _ => "Error: action is required. Valid actions: create, list, update_step, complete, pause, resume, delete."
        };
    }

    private async Task<string> CreateAsync(JsonElement args, CancellationToken ct)
    {
        var title = args.TryGetProperty("title", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Error: title is required for create.";
        }

        var goal = new Goal { Title = title };

        if (args.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
        {
            goal.Description = d.GetString();
        }

        if (args.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var stepEl in stepsEl.EnumerateArray())
            {
                var text = stepEl.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    goal.Steps.Add(new GoalStep { Text = text });
                }
            }
        }

        var goals = await _storage.LoadAsync(ct);
        goals.Add(goal);
        await _storage.SaveAsync(goals, ct);

        LogGoalCreated(_logger, goal.Id, goal.Title);

        var sb = new StringBuilder();
        sb.Append($"Goal created: {goal.Id} — {goal.Title}");
        if (goal.Steps.Count > 0)
        {
            sb.Append($" ({goal.Steps.Count} steps)");
        }

        return sb.ToString();
    }

    private async Task<string> ListAsync(JsonElement args, CancellationToken ct)
    {
        var statusFilter = args.TryGetProperty("status", out var s) ? s.GetString() : "active";
        var goals = await _storage.LoadAsync(ct);

        var filtered = statusFilter switch
        {
            "all" => goals.Where(g => g.Status != GoalStatus.Deleted).ToList(),
            "paused" => goals.Where(g => g.Status == GoalStatus.Paused).ToList(),
            _ => goals.Where(g => g.Status == GoalStatus.Active).ToList()
        };

        if (filtered.Count == 0)
        {
            return statusFilter == "all" ? "No goals found." : $"No {statusFilter} goals found.";
        }

        var sb = new StringBuilder();
        foreach (var g in filtered)
        {
            var doneCount = g.Steps.Count(step => step.Done);
            var stepInfo = g.Steps.Count > 0 ? $" [{doneCount}/{g.Steps.Count} steps]" : "";
            sb.AppendLine($"- [{g.Id}] {g.Title}{stepInfo} ({g.Status})");

            foreach (var step in g.Steps)
            {
                var mark = step.Done ? "x" : " ";
                sb.AppendLine($"  [{mark}] {step.Text}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string> UpdateStepAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Error: id is required for update_step.";
        }

        if (!args.TryGetProperty("step_index", out var siEl) || !siEl.TryGetInt32(out var stepIndex))
        {
            return "Error: step_index (integer) is required for update_step.";
        }

        if (!args.TryGetProperty("done", out var doneEl) || doneEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return "Error: done (boolean) is required for update_step.";
        }

        var done = doneEl.GetBoolean();

        var goals = await _storage.LoadAsync(ct);
        var goal = goals.Find(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        if (goal is null)
        {
            return $"Error: goal '{id}' not found.";
        }

        if (stepIndex < 0 || stepIndex >= goal.Steps.Count)
        {
            return $"Error: step_index {stepIndex} out of range (0..{goal.Steps.Count - 1}).";
        }

        goal.Steps[stepIndex].Done = done;
        goal.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await _storage.SaveAsync(goals, ct);

        var stepText = goal.Steps[stepIndex].Text;
        return $"Step {stepIndex} of goal '{goal.Title}' marked as {(done ? "done" : "not done")}: {stepText}";
    }

    private async Task<string> SetStatusAsync(JsonElement args, GoalStatus newStatus, CancellationToken ct)
    {
        var id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return $"Error: id is required for {newStatus.ToString().ToLowerInvariant()}.";
        }

        var goals = await _storage.LoadAsync(ct);
        var goal = goals.Find(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        if (goal is null)
        {
            return $"Error: goal '{id}' not found.";
        }

        if (!Goal.CanTransition(goal.Status, newStatus))
        {
            var valid = Goal.GetValidTransitions(goal.Status);
            var allowed = valid.Length > 0
                ? string.Join(", ", valid.Select(s => s.ToString().ToLowerInvariant()))
                : "none (terminal state)";
            return $"Error: cannot transition goal '{id}' from {goal.Status.ToString().ToLowerInvariant()} to " +
                   $"{newStatus.ToString().ToLowerInvariant()}. Valid transitions: {allowed}.";
        }

        goal.Status = newStatus;
        goal.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await _storage.SaveAsync(goals, ct);

        return $"Goal {goal.Id} ({goal.Title}) marked as {newStatus.ToString().ToLowerInvariant()}.";
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Goal created: {GoalId} — {Title}")]
    private static partial void LogGoalCreated(ILogger logger, string goalId, string title);
}