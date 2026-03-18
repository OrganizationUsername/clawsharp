using Intellenum;

namespace Clawsharp.Ipc;

/// <summary>Well-known IPC error codes.</summary>
[Intellenum<string>]
internal partial class IpcError
{
    public static readonly IpcError AuthFailed = new("auth_failed");
    public static readonly IpcError TooManyConnections = new("too_many_connections");
}
