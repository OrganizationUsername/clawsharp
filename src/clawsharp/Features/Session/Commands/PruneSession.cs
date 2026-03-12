using Clawsharp.Core.Sessions;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Session.Commands;

/// <summary>
/// Prunes stale or excess messages from a session. Saves to disk only if
/// messages were actually removed. Returns whether pruning occurred.
/// </summary>
[Handler]
public static partial class PruneSession
{
    public sealed record Command(Core.Sessions.Session Session, int? MaxMessages, int? MaxAgeDays);

    private static async ValueTask<bool> HandleAsync(
        Command command,
        SessionManager sessionManager,
        CancellationToken ct)
    {
        if (!command.Session.Prune(command.MaxMessages, command.MaxAgeDays))
        {
            return false;
        }

        await sessionManager.SaveAsync(command.Session, ct);
        return true;
    }
}