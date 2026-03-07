using Clawsharp.Memory;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Memory.Commands;

/// <summary>
/// Clears all stored facts from memory.
/// History entries are WORM and preserved across clears.
/// Thin wrapper around <see cref="IMemory.ClearAsync"/>.
/// </summary>
[Handler]
public static partial class ClearMemory
{
    public sealed record Command;

    private static async ValueTask HandleAsync(
        Command command,
        IMemory memory,
        CancellationToken ct)
    {
        await memory.ClearAsync(ct);
    }
}