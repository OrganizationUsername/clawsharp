using System.Text.Json;

namespace Clawsharp.Tools;

public abstract class Tool
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string ParametersSchemaJson { get; }

    /// <summary>
    ///     Sensitivity classification for channel-based access control.
    ///     Override in derived classes to set the appropriate level.
    ///     Defaults to <see cref="ToolSensitivity.Medium"/>.
    /// </summary>
    public virtual ToolSensitivity Sensitivity => ToolSensitivity.Medium;

    public abstract Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default);

    public Core.ToolDefinition ToDefinition()
    {
        return new(Name, Description, ParametersSchemaJson);
    }
}