using System.Text.Json;

namespace Clawsharp.Tools;

public abstract class Tool
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string ParametersSchemaJson { get; }

    public abstract Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default);

    public Core.ToolDefinition ToDefinition()
    {
        return new(Name, Description, ParametersSchemaJson);
    }
}