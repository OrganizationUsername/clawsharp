using System.Text.Json;
using Clawsharp.Memory;

namespace Clawsharp.Tools.Memory;

public sealed class MemoryReadTool : Tool
{
    private readonly IMemory _memory;

    public MemoryReadTool(IMemory memory)
    {
        _memory = memory;
    }

    public override string Name => "memory_read";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description => "Read all facts from memory.";

    public override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var ctx = await _memory.GetContextAsync(ct);
        return ctx ?? "(memory is empty)";
    }
}