using Clawsharp.Tools;
using Immediate.Handlers.Shared;

namespace Clawsharp.Features.Tools.Commands;

/// <summary>
/// Executes a tool by name with the given JSON arguments.
/// Thin wrapper around <see cref="IToolRegistry.ExecuteAsync"/>.
/// </summary>
[Handler]
public static partial class ExecuteToolCall
{
    public sealed record Command(string ToolName, string ArgumentsJson);

    private static async ValueTask<string> HandleAsync(
        Command command,
        IToolRegistry toolRegistry,
        CancellationToken ct)
    {
        return await toolRegistry.ExecuteAsync(command.ToolName, command.ArgumentsJson, ct);
    }
}