using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Slack;

/// <summary>Envelope received via Slack Socket Mode WebSocket connection.</summary>
internal sealed class SlackSocketEnvelope
{
    /// <summary>Unique envelope identifier for acknowledgement.</summary>
    [JsonPropertyName("envelope_id")]
    public string? EnvelopeId { get; init; }

    /// <summary>Envelope type (e.g. "events_api").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>The event payload as a raw JSON element.</summary>
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}