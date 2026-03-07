using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Delta content within an Anthropic streaming event.</summary>
internal sealed class StreamDelta
{
    /// <summary>Delta type ("text_delta" or "input_json_delta").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Text delta -- set when delta.type == "text_delta".</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Partial JSON arguments -- set when delta.type == "input_json_delta".</summary>
    [JsonPropertyName("partial_json")]
    public string? PartialJson { get; init; }

    /// <summary>Thinking delta -- set when delta.type == "thinking_delta".</summary>
    [JsonPropertyName("thinking")]
    public string? ThinkingDelta { get; init; }
}