using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Response from the Matrix send message API.</summary>
internal sealed class MatrixSendMessageResponse
{
    /// <summary>The event ID of the sent message.</summary>
    [JsonPropertyName("event_id")]
    public string? EventId { get; init; }
}