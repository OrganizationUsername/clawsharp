using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Line;

/// <summary>
/// Webhook request body from LINE Messaging API.
/// </summary>
internal sealed class LineWebhookRequest
{
    [JsonPropertyName("destination")]
    public string? Destination { get; init; }

    [JsonPropertyName("events")]
    public List<LineEvent>? Events { get; init; }
}

/// <summary>
/// Individual event from a LINE webhook callback.
/// </summary>
internal sealed class LineEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("source")]
    public LineSource? Source { get; init; }

    [JsonPropertyName("message")]
    public LineMessage? Message { get; init; }

    [JsonPropertyName("replyToken")]
    public string? ReplyToken { get; init; }
}

/// <summary>
/// Source (sender) of a LINE event.
/// </summary>
internal sealed class LineSource
{
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>
/// Message content from a LINE event.
/// </summary>
internal sealed class LineMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>
/// Push message request body for POST https://api.line.me/v2/bot/message/push.
/// </summary>
internal sealed class LinePushRequest
{
    [JsonPropertyName("to")]
    public string To { get; init; } = "";

    [JsonPropertyName("messages")]
    public List<LineTextMessage> Messages { get; init; } = [];
}

/// <summary>
/// A text message object for LINE send API.
/// </summary>
internal sealed class LineTextMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}

[JsonSerializable(typeof(LineWebhookRequest))]
[JsonSerializable(typeof(LinePushRequest))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class LineJsonContext : JsonSerializerContext;