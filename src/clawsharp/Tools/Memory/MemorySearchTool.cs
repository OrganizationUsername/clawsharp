using System.Text.Json;
using Clawsharp.Memory;

using Clawsharp.Tools;
namespace Clawsharp.Tools.Memory;

public sealed class MemorySearchTool : Tool
{
    private readonly IMemory _memory;

    public MemorySearchTool(IMemory memory)
    {
        _memory = memory;
    }

    public override string Name => "memory_search";

    public override string Description => "Search persistent memory for relevant facts.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "query": { "type": "string", "description": "Search query" },
                                                       "n": { "type": "integer", "description": "Number of results (default 5)" }
                                                     },
                                                     "required": ["query"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var n = arguments.TryGetProperty("n", out var nProp) && nProp.TryGetInt32(out var nVal) ? nVal : 5;
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: query is required.";
        }

        var results = await _memory.SearchAsync(query, n, ct);
        return results.Count > 0 ? string.Join("\n", results) : "No matching facts found.";
    }
}