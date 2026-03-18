using Clawsharp.Core.Sessions;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Session.Commands;

/// <summary>
/// Persists the current session state to disk via atomic file write.
/// </summary>
[Handler]
public static partial class SaveSession
{
    public sealed record Command(Core.Sessions.Session Session);

    private static async ValueTask HandleAsync(
        Command command,
        SessionStore sessionManager,
        CancellationToken ct)
    {
        await sessionManager.SaveAsync(command.Session, ct);
    }
}