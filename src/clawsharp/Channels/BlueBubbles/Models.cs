using System.Text.Json.Serialization;

namespace Clawsharp.Channels.BlueBubbles;

/// <summary>
/// Response wrapper from GET /api/v1/message on BlueBubbles server.
/// </summary>
internal sealed class BlueBubblesMessageResponse
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("data")]
    public List<BlueBubblesMessage>? Data { get; init; }
}

/// <summary>
/// Individual message from BlueBubbles server.
/// </summary>
internal sealed class BlueBubblesMessage
{
    [JsonPropertyName("guid")]
    public string Guid { get; init; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("handle")]
    public BlueBubblesHandle? Handle { get; init; }

    [JsonPropertyName("isFromMe")]
    public bool IsFromMe { get; init; }

    [JsonPropertyName("dateCreated")]
    public long DateCreated { get; init; }
}

/// <summary>
/// Handle (contact) information from BlueBubbles.
/// </summary>
internal sealed class BlueBubblesHandle
{
    [JsonPropertyName("address")]
    public string Address { get; init; } = "";
}

/// <summary>
/// Request body for POST /api/v1/message/text on BlueBubbles server.
/// </summary>
internal sealed class BlueBubblesSendRequest
{
    [JsonPropertyName("chatGuid")]
    public string ChatGuid { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("method")]
    public string Method { get; init; } = "private-api";
}

[JsonSerializable(typeof(BlueBubblesMessageResponse))]
[JsonSerializable(typeof(BlueBubblesSendRequest))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class BlueBubblesJsonContext : JsonSerializerContext;