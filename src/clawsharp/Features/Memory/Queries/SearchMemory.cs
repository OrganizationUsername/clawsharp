using Clawsharp.Memory;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Memory.Queries;

/// <summary>
/// Searches memory for facts matching the given query text.
/// Thin wrapper around <see cref="IMemory.SearchAsync"/>.
/// </summary>
[Handler]
public static partial class SearchMemory
{
    public sealed record Query(string SearchText, int Limit = 10);

    private static async ValueTask<IReadOnlyList<string>> HandleAsync(
        Query query,
        IMemory memory,
        CancellationToken ct)
    {
        return await memory.SearchAsync(query.SearchText, query.Limit, ct);
    }
}