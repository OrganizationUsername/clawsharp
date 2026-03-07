using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Collection of rooms grouped by membership type in a Matrix sync response.</summary>
internal sealed class MatrixRoomCollection
{
    /// <summary>Rooms the user has joined, keyed by room ID.</summary>
    [JsonPropertyName("join")]
    public Dictionary<string, MatrixJoinedRoom>? Join { get; init; }
}