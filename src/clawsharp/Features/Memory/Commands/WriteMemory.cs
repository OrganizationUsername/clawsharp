using Clawsharp.Memory;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Memory.Commands;

/// <summary>
/// Appends a fact to the memory store.
/// Thin wrapper around <see cref="IMemory.AppendFactAsync"/>.
/// </summary>
[Handler]
public static partial class WriteMemory
{
    public sealed record Command(string Fact);

    private static async ValueTask HandleAsync(
        Command command,
        IMemory memory,
        CancellationToken ct)
    {
        await memory.AppendFactAsync(command.Fact, ct);
    }
}