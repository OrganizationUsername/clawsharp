using System.Text.Json;
using Clawsharp.Memory;

namespace Clawsharp.Tools.Memory;

public sealed class HistoryAppendTool(IMemory memory) : Tool
{
    public override string Name => "history_append";

    public override string Description => "Append a summary of this conversation to the long-term history log.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "summary": { "type": "string", "description": "Conversation summary to store" }
                                                     },
                                                     "required": ["summary"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var summary = arguments.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(summary))
        {
            return "Error: summary is required.";
        }

        await memory.AppendHistoryAsync(summary, ct);
        return "History updated.";
    }
}