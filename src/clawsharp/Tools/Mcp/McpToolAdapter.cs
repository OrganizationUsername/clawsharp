using System.Text.Json;

namespace Clawsharp.Tools.Mcp;

/// <summary>
/// Adapts an MCP server tool as a clawsharp <see cref="Tool"/>.
/// Delegates execution to the owning <see cref="McpClient"/>.
/// </summary>
public sealed class McpToolAdapter(McpClient client, McpToolSchema schema) : Tool
{
    public override string Name => schema.Name;

    public override string Description => schema.Description ?? "";

    public override string ParametersSchemaJson { get; } = schema.InputSchema.GetRawText();

    // NOTE: MCP tool inputs are passed through unvalidated. The MCP server is responsible
    // for input validation. Schema is available via tool definition but not enforced client-side.
    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var argumentsJson = arguments.GetRawText();
        try
        {
            return await client.CallToolAsync(Name, argumentsJson, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // External cancellation (e.g. shutdown, timeout) — propagate so the caller
            // can honour the cancellation request cleanly.
            throw;
        }
        catch (OperationCanceledException)
        {
            // MCP server-side or transport-level cancellation — return error to the LLM
            // rather than crashing the tool loop.
            return "MCP tool execution was cancelled by the server.";
        }
    }
}