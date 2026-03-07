using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Matrix user identifier for login requests.</summary>
internal sealed class MatrixUserIdentifier
{
    /// <summary>Identifier type (always "m.id.user").</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "m.id.user";

    /// <summary>The Matrix user ID or localpart.</summary>
    [JsonPropertyName("user")]
    public string User { get; init; } = "";
}