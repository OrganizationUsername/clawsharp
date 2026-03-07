using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Web;

/// <summary>Response to a web chat request containing the assistant's reply.</summary>
internal sealed class WebChatResponse
{
    /// <summary>The assistant's reply text.</summary>
    [JsonPropertyName("reply")]
    public string Reply { get; init; } = "";

    /// <summary>The session identifier for this conversation.</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";
}