using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Response from the Matrix login API.</summary>
internal sealed class MatrixLoginResponse
{
    /// <summary>Access token for subsequent API calls.</summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    /// <summary>The fully-qualified Matrix user ID.</summary>
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }
}