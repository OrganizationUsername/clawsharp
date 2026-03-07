using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Session.Queries;

/// <summary>
/// Loads an existing session by ID, or creates a new empty session if none exists.
/// </summary>
[Handler]
public static partial class LoadSession
{
    public sealed record Query(string SessionId);

    private static async ValueTask<Core.Sessions.Session> HandleAsync(
        Query query,
        SessionManager sessionManager,
        CancellationToken ct)
    {
        return await sessionManager.LoadOrCreateAsync(query.SessionId, ct);
    }
}