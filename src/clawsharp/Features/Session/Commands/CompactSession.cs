using Clawsharp.Core;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Providers;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Session.Commands;

/// <summary>
/// Compacts session history by summarizing older messages via the LLM,
/// keeping recent messages verbatim. Replaces the session's message list
/// with the compacted result and persists to disk.
/// </summary>
[Handler]
public static partial class CompactSession
{
    public sealed record Command(
        Core.Sessions.Session Session,
        IProvider Provider,
        string Model,
        int KeepRecent = 20,
        int MaxSummaryChars = 2000,
        int MaxSourceChars = 12000);

    private static async ValueTask<IReadOnlyList<ChatMessage>> HandleAsync(
        Command command,
        CompactionService compactionService,
        SessionManager sessionManager,
        CancellationToken ct)
    {
        var compacted = await compactionService.CompactAsync(
            command.Session.Messages,
            command.Provider,
            command.Model,
            command.KeepRecent,
            command.MaxSummaryChars,
            command.MaxSourceChars,
            ct);

        command.Session.Messages.Clear();
        command.Session.Messages.AddRange(compacted);
        await sessionManager.SaveAsync(command.Session, ct);

        return compacted;
    }
}