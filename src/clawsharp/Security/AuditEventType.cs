using Intellenum;

namespace Clawsharp.Security;

/// <summary>Well-known audit event type identifiers.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class AuditEventType
{
    public static readonly AuditEventType CommandExecution = new("command_execution");
    public static readonly AuditEventType FileAccess = new("file_access");
    public static readonly AuditEventType ConfigChange = new("config_change");
    public static readonly AuditEventType AuthSuccess = new("auth_success");
    public static readonly AuditEventType AuthFailure = new("auth_failure");
    public static readonly AuditEventType PolicyViolation = new("policy_violation");
    public static readonly AuditEventType SecurityEvent = new("security_event");
    public static readonly AuditEventType CostBudget = new("cost_budget");
    public static readonly AuditEventType RateLimit = new("rate_limit");
}
