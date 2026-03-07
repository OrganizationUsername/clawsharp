using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Web;

/// <summary>
/// WebSocket streaming frame sent per token during response generation.
/// Clients differentiate streaming frames from full messages by the presence of the "delta" key.
/// </summary>
internal sealed class WebStreamDelta
{
    /// <summary>A single token or text fragment from the streaming response.</summary>
    [JsonPropertyName("delta")]
    public string Delta { get; init; } = "";
}