using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Web;

/// <summary>Response from the POST /pair endpoint for web client authentication.</summary>
internal sealed class WebPairResponse
{
    /// <summary>Whether pairing was successful.</summary>
    [JsonPropertyName("paired")]
    public bool Paired { get; init; }

    /// <summary>Bearer token for subsequent authenticated requests (only on success).</summary>
    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }

    /// <summary>Human-readable message about the pairing result.</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    /// <summary>Error code when pairing fails.</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}