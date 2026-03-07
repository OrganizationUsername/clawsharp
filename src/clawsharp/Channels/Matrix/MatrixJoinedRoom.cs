using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Data for a single joined room in a Matrix sync response.</summary>
internal sealed class MatrixJoinedRoom
{
    /// <summary>Timeline events for this room.</summary>
    [JsonPropertyName("timeline")]
    public MatrixRoomTimeline? Timeline { get; init; }
}