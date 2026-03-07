using System.Text.Json.Serialization;

namespace Clawsharp.Security;

public sealed record AuditEvent
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "";

    [JsonPropertyName("actor")]
    public AuditActor? Actor { get; init; }

    [JsonPropertyName("action")]
    public AuditAction? Action { get; init; }

    [JsonPropertyName("result")]
    public AuditResult? Result { get; init; }

    [JsonPropertyName("security")]
    public AuditSecurity? Security { get; init; }
}

public sealed record AuditActor
{
    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

public sealed record AuditAction
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("risk_level")]
    public string? RiskLevel { get; init; }

    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

public sealed record AuditResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public sealed record AuditSecurity
{
    [JsonPropertyName("policy_violation")]
    public bool PolicyViolation { get; init; }

    [JsonPropertyName("sandbox_backend")]
    public string? SandboxBackend { get; init; }

    [JsonPropertyName("ssrf_blocked")]
    public bool SsrfBlocked { get; init; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; init; }
}

public static class AuditEventTypes
{
    public const string CommandExecution = "command_execution";

    public const string FileAccess = "file_access";

    public const string ConfigChange = "config_change";

    public const string AuthSuccess = "auth_success";

    public const string AuthFailure = "auth_failure";

    public const string PolicyViolation = "policy_violation";

    public const string SecurityEvent = "security_event";

    public const string CostBudget = "cost_budget";

    public const string RateLimit = "rate_limit";
}