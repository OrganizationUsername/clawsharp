using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Web;

/// <summary>WebSocket first-frame auth response (sent by server after client auth frame).</summary>
public sealed class WebAuthResponse
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}