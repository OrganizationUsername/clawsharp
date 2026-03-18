using Clawsharp.Config;
using Clawsharp.Security;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Features.Chat.Commands;

/// <summary>
/// Applies post-processing security checks to the LLM reply before delivery:
/// canary token exfiltration detection and credential/secret leak scanning.
/// Encapsulates the sanitization logic previously in
/// <c>AgentLoop.PostProcessReplyAsync</c> (lines 654-676).
/// </summary>
[Handler]
public static partial class SanitizeReply
{
    /// <summary>Logger category for DI resolution (static types cannot be used as type arguments).</summary>
    public sealed class Log;

    public sealed record Command(
        /// <summary>The raw LLM reply text to sanitize.</summary>
        string Reply,
        /// <summary>The canary guard instance for this turn, or null if canary is disabled.</summary>
        CanaryGuard? CanaryGuard,
        /// <summary>Channel name for audit logging.</summary>
        string ChannelName,
        /// <summary>Sender ID for audit logging.</summary>
        string SenderId);

    public sealed record Result(
        /// <summary>The sanitized reply with any leaked secrets or canary tokens redacted.</summary>
        string SanitizedReply);

    private static async ValueTask<Result> HandleAsync(
        Command command,
        AuditLogger auditLogger,
        IOptions<AppConfig> appConfig,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        var reply = command.Reply;

        // Canary exfiltration check — detect if the LLM leaked system prompt content.
        if (command.CanaryGuard is not null && command.CanaryGuard.CheckOutput(reply))
        {
            reply = command.CanaryGuard.Redact(reply);
            await auditLogger.LogSecurityEventAsync(
                "Canary token exfiltration detected \u2014 LLM leaked system prompt content",
                command.ChannelName, command.SenderId, ct);
            logger.LogWarning(
                "Canary exfiltration detected on channel {Channel} for sender {Sender}",
                command.ChannelName, command.SenderId);
        }

        // Scan outbound reply for leaked credentials/secrets before delivery.
        var leakSensitivity = appConfig.Value.Security?.LeakDetector?.Sensitivity ?? 0.7;
        if (leakSensitivity >= 0)
        {
            var leakResult = LeakDetector.Scan(reply, leakSensitivity);
            if (!leakResult.IsClean)
            {
                await auditLogger.LogSecurityEventAsync(
                    $"LLM output leak detected: {string.Join(", ", leakResult.Patterns)}",
                    command.ChannelName, command.SenderId, ct);
                logger.LogWarning(
                    "Leak detected in reply for {Channel}:{Sender}: {Patterns}",
                    command.ChannelName, command.SenderId,
                    string.Join(", ", leakResult.Patterns));
                reply = leakResult.Redacted;
            }
        }

        return new Result(reply);
    }
}