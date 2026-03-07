using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Response from the Matrix sync API.</summary>
internal sealed class MatrixSyncResponse
{
    /// <summary>Token for the next sync request (pagination).</summary>
    [JsonPropertyName("next_batch")]
    public string? NextBatch { get; init; }

    /// <summary>Room data grouped by membership type.</summary>
    [JsonPropertyName("rooms")]
    public MatrixRoomCollection? Rooms { get; init; }
}