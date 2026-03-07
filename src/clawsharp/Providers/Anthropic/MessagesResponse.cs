using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Response from the Anthropic Messages API.</summary>
internal sealed class MessagesResponse
{
    /// <summary>Content blocks in the response (text, tool_use, etc.).</summary>
    [JsonPropertyName("content")]
    public List<JsonElement> Content { get; init; } = [];

    /// <summary>Reason the model stopped ("end_turn", "tool_use", "max_tokens").</summary>
    [JsonPropertyName("stop_reason")]
    public string StopReason { get; init; } = "end_turn";

    /// <summary>Token usage statistics.</summary>
    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }
}