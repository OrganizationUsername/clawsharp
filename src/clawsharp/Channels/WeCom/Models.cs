using System.Text.Json.Serialization;

namespace Clawsharp.Channels.WeCom;

// ── Inbound Message DTOs (decrypted JSON from WeCom AI Bot) ──────────

/// <summary>Top-level decrypted message from WeCom AI Bot.</summary>
internal sealed class WeComBotMessage
{
    [JsonPropertyName("msgid")]
    public string? MsgId { get; init; }

    [JsonPropertyName("aibotid")]
    public string? AiBotId { get; init; }

    [JsonPropertyName("chatid")]
    public string? ChatId { get; init; }

    [JsonPropertyName("chattype")]
    public string? ChatType { get; init; }

    [JsonPropertyName("from")]
    public WeComSender? From { get; init; }

    [JsonPropertyName("response_url")]
    public string? ResponseUrl { get; init; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; init; }

    [JsonPropertyName("text")]
    public WeComTextContent? Text { get; init; }

    [JsonPropertyName("voice")]
    public WeComVoiceContent? Voice { get; init; }

    [JsonPropertyName("image")]
    public WeComImageContent? Image { get; init; }

    [JsonPropertyName("mixed")]
    public WeComMixedContent? Mixed { get; init; }
}

/// <summary>Sender information within a WeCom AI Bot message.</summary>
internal sealed class WeComSender
{
    [JsonPropertyName("userid")]
    public string? UserId { get; init; }
}

/// <summary>Text content within a WeCom AI Bot message.</summary>
internal sealed class WeComTextContent
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>Voice content within a WeCom AI Bot message (pre-transcribed).</summary>
internal sealed class WeComVoiceContent
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>Image content within a WeCom AI Bot message.</summary>
internal sealed class WeComImageContent
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>Mixed content within a WeCom AI Bot message.</summary>
internal sealed class WeComMixedContent
{
    [JsonPropertyName("msg_item")]
    public List<WeComMixedItem>? MsgItem { get; init; }
}

/// <summary>A single item within mixed content.</summary>
internal sealed class WeComMixedItem
{
    [JsonPropertyName("msgtype")]
    public string? MsgType { get; init; }

    [JsonPropertyName("text")]
    public WeComTextContent? Text { get; init; }

    [JsonPropertyName("image")]
    public WeComImageContent? Image { get; init; }
}

// ── Outbound Reply DTOs ──────────────────────────────────────────────

/// <summary>Reply message posted to the WeCom AI Bot response_url.</summary>
internal sealed class WeComReplyMessage
{
    [JsonPropertyName("msgtype")]
    public string MsgType { get; init; } = "text";

    [JsonPropertyName("text")]
    public WeComReplyText? Text { get; init; }
}

/// <summary>Text body for a WeCom reply message.</summary>
internal sealed class WeComReplyText
{
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

/// <summary>WeCom API response envelope.</summary>
internal sealed class WeComApiResponse
{
    [JsonPropertyName("errcode")]
    public int ErrCode { get; init; }

    [JsonPropertyName("errmsg")]
    public string? ErrMsg { get; init; }
}

// ── JSON Context ─────────────────────────────────────────────────────

[JsonSerializable(typeof(WeComBotMessage))]
[JsonSerializable(typeof(WeComReplyMessage))]
[JsonSerializable(typeof(WeComApiResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class WeComBotJsonContext : JsonSerializerContext;