using Clawsharp.Config;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Security;
using Immediate.Handlers.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;

namespace Clawsharp.Features.Chat.Commands;

/// <summary>
/// Applies prompt injection scanning/wrapping and vision-drop logic to an inbound message.
/// Non-CLI messages are scanned for injection patterns and wrapped in XML delimiters.
/// Images are dropped with a system note when the provider does not support vision.
/// Encapsulates the logic previously in <c>AgentLoop.ApplySecurityGuards</c> (lines 468-503).
/// </summary>
[Handler]
public static partial class ApplySecurityGuards
{
    /// <summary>Logger category for DI resolution (static types cannot be used as type arguments).</summary>
    public sealed class Log;

    public sealed record Command(
        InboundMessage Inbound,
        /// <summary>Whether the current LLM provider supports vision/image inputs.</summary>
        bool SupportsVision,
        /// <summary>Name of the current LLM provider (for logging dropped images).</summary>
        string ProviderName);

    public sealed record Result(
        /// <summary>The (possibly wrapped/sanitized) user text ready for the LLM.</summary>
        string UserText,
        /// <summary>Image attachments to include, or null if dropped.</summary>
        IReadOnlyList<ImageAttachment>? Images,
        /// <summary>True if the message was blocked by the injection guard.</summary>
        bool Blocked);

    private static ValueTask<Result> HandleAsync(
        Command command,
        AuditLogger auditLogger,
        IOptions<AgentDefaults> defaults,
        IOptions<AppConfig> appConfig,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        var inbound = command.Inbound;
        var cfg = defaults.Value;
        var userText = inbound.Text;

        // Prompt injection guard: scan and wrap non-CLI user messages.
        if (cfg.PromptInjectionGuard && inbound.Channel != ChannelName.Cli)
        {
            var guardMode = appConfig.Value.Security?.PromptGuard?.Mode ?? "warn";
            var injAction = PromptGuard.ScanAndApply(
                ref userText, "user message", guardMode, auditLogger,
                inbound.Channel.Value, inbound.SenderId, ct);

            if (injAction == InjectionAction.Block)
            {
                return ValueTask.FromResult(new Result(userText, null, Blocked: true));
            }

            if (injAction != InjectionAction.None)
            {
                logger.LogWarning(
                    "Potential prompt injection in user message: {Preview}",
                    userText[..Math.Min(50, userText.Length)]);
            }

            userText = PromptGuard.WrapUserMessage(userText);
        }

        // Vision drop warning: strip images if provider doesn't support vision.
        IReadOnlyList<ImageAttachment>? imagesToInclude = inbound.Images;
        if (inbound.Images is { Count: > 0 } && !command.SupportsVision)
        {
            logger.LogWarning(
                "Dropping {ImageCount} image(s) from {Channel} — provider {Provider} does not support vision",
                inbound.Images.Count, inbound.Channel.Value, command.ProviderName);
            imagesToInclude = null;
            userText +=
                "\n\n[System note: The user attached image(s) but the current provider does not support vision. Inform the user that their images could not be processed and suggest switching to a vision-capable provider like gpt-4o, claude-3, or gemini.]";
        }

        return ValueTask.FromResult(new Result(userText, imagesToInclude, Blocked: false));
    }
}