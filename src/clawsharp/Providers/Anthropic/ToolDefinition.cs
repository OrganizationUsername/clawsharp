using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Tool definition in the Anthropic format (name, description, input_schema).</summary>
internal sealed class ToolDefinition
{
    /// <summary>The tool name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Description of what the tool does.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>JSON Schema for the tool's input parameters.</summary>
    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; init; }

    /// <summary>
    /// When set on the <em>last</em> tool in the list, caches the entire tools array
    /// up to and including this entry.
    /// </summary>
    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CacheControl? CacheControl { get; set; }
}