using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Session.Commands;

/// <summary>
/// Clears all messages from a session and persists the empty state.
/// </summary>
[Handler]
public static partial class ClearSession
{
    public sealed record Command(Core.Sessions.Session Session);

    private static async ValueTask HandleAsync(
        Command command,
        SessionManager sessionManager,
        CancellationToken ct)
    {
        command.Session.Messages.Clear();
        await sessionManager.SaveAsync(command.Session, ct);
    }
}