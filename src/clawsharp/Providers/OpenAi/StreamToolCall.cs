using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Incremental tool call data within a streaming delta.</summary>
internal sealed class StreamToolCall
{
    /// <summary>Index of this tool call in the tool_calls array.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>Tool call ID (only present in the first chunk for this tool call).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Incremental function data.</summary>
    [JsonPropertyName("function")]
    public StreamFunction? Function { get; init; }
}