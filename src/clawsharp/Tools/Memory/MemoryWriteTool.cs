using System.Text.Json;
using Clawsharp.Memory;
using Clawsharp.Security;

namespace Clawsharp.Tools.Memory;

public sealed class MemoryWriteTool : Tool
{
    private readonly IMemory _memory;

    public MemoryWriteTool(IMemory memory)
    {
        _memory = memory;
    }

    public override string Name => "memory_write";

    public override string Description => "Save an important fact to persistent memory for future conversations.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "fact": { "type": "string", "description": "The fact to remember" }
                                                     },
                                                     "required": ["fact"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var fact = arguments.TryGetProperty("fact", out var f) ? f.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(fact))
        {
            return "Error: fact is required.";
        }

        // Scrub potential secrets from LLM-written memory content
        var scrubResult = LeakDetector.Scan(fact, 0.5);
        if (!scrubResult.IsClean)
        {
            fact = scrubResult.Redacted;
        }

        await _memory.AppendFactAsync(fact, ct);
        return $"Saved: {fact}";
    }
}