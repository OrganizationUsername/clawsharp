using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>A single event in a Matrix room timeline.</summary>
internal sealed class MatrixRoomEvent
{
    /// <summary>Event type (e.g. "m.room.message").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>Unique event identifier.</summary>
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = "";

    /// <summary>Fully-qualified Matrix ID of the event sender.</summary>
    [JsonPropertyName("sender")]
    public string Sender { get; init; } = "";

    /// <summary>Event content as a raw JSON element.</summary>
    [JsonPropertyName("content")]
    public JsonElement Content { get; init; }
}