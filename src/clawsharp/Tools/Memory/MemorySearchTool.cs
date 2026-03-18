using System.Text.Json;
using Clawsharp.Memory;

namespace Clawsharp.Tools.Memory;

public sealed class MemorySearchTool(IMemory memory) : Tool
{
    public override string Name => "memory_search";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

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

        var results = await memory.SearchAsync(query, n, ct);
        if (results.Count > 0)
        {
            return string.Join("\n", results);
        }

        return "No matching facts found.";
    }
}