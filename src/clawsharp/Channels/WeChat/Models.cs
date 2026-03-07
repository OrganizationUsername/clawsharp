using System.Text.Json.Serialization;

namespace Clawsharp.Channels.WeChat;

/// <summary>
/// Incoming message from a WeCom bridge (polling endpoint).
/// </summary>
internal sealed class WeChatBridgeMessage
{
    [JsonPropertyName("fromUser")]
    public string FromUser { get; init; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}

/// <summary>
/// Send request body for the WeCom bridge endpoint.
/// </summary>
internal sealed class WeChatBridgeSendRequest
{
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("toUser")]
    public string ToUser { get; init; } = "";
}

/// <summary>
/// WeCom robot webhook request body for POST /cgi-bin/webhook/send.
/// </summary>
internal sealed class WeChatWebhookRequest
{
    [JsonPropertyName("msgtype")]
    public string MsgType { get; init; } = "text";

    [JsonPropertyName("text")]
    public WeChatWebhookText Text { get; init; } = new();
}

/// <summary>
/// Text content for WeCom robot webhook.
/// </summary>
internal sealed class WeChatWebhookText
{
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

[JsonSerializable(typeof(List<WeChatBridgeMessage>))]
[JsonSerializable(typeof(WeChatBridgeSendRequest))]
[JsonSerializable(typeof(WeChatWebhookRequest))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class WeChatJsonContext : JsonSerializerContext;