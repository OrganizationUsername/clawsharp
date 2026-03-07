using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Web;

/// <summary>
/// WebSocket frame sent to indicate the agent is thinking (processing) or has stopped.
/// Clients can display a typing indicator when <see cref="Value"/> is true.
/// </summary>
internal sealed class WebThinkingFrame
{
    /// <summary>Frame type — always "thinking".</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "thinking";

    /// <summary>True when the agent starts processing; false when it stops.</summary>
    [JsonPropertyName("value")]
    public bool Value { get; init; }
}