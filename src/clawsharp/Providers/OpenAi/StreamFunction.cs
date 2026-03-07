using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Incremental function call data within a streaming tool call.</summary>
internal sealed class StreamFunction
{
    /// <summary>Function name (only present in the first chunk).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Incremental JSON arguments fragment.</summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}