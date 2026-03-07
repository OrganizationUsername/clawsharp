using System.Text.Json.Serialization;

namespace Clawsharp.Channels.WhatsApp;

/// <summary>
/// Incoming message from the WhatsApp bridge (whatsapp-web.js REST API pattern).
/// Returned by GET {bridgeUrl}/api/messages/unread.
/// </summary>
internal sealed class WhatsAppIncomingMessage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("from")]
    public string From { get; init; } = "";

    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("fromMe")]
    public bool FromMe { get; init; }

    [JsonPropertyName("senderName")]
    public string? SenderName { get; init; }

    /// <summary>Message type: "chat", "ptt" (voice note), "audio", "image", etc.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>True when the message contains a media attachment.</summary>
    [JsonPropertyName("hasMedia")]
    public bool HasMedia { get; init; }
}

/// <summary>
/// Request body for POST {bridgeUrl}/api/sendMessage.
/// </summary>
internal sealed class WhatsAppSendRequest
{
    [JsonPropertyName("chatId")]
    public string ChatId { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

/// <summary>
/// Response from POST {bridgeUrl}/api/sendMessage.
/// </summary>
internal sealed class WhatsAppSendResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>Response from GET {bridgeUrl}/api/downloadMedia/{messageId}.</summary>
internal sealed class WhatsAppMediaResponse
{
    /// <summary>Base64-encoded audio data.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>MIME type, e.g. "audio/ogg; codecs=opus".</summary>
    [JsonPropertyName("mimetype")]
    public string? Mimetype { get; init; }
}

[JsonSerializable(typeof(List<WhatsAppIncomingMessage>))]
[JsonSerializable(typeof(WhatsAppSendRequest))]
[JsonSerializable(typeof(WhatsAppSendResponse))]
[JsonSerializable(typeof(WhatsAppMediaResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class WhatsAppJsonContext : JsonSerializerContext;