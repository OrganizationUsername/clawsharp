using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Timeline containing recent events for a joined Matrix room.</summary>
internal sealed class MatrixRoomTimeline
{
    /// <summary>List of timeline events.</summary>
    [JsonPropertyName("events")]
    public List<MatrixRoomEvent>? Events { get; init; }
}