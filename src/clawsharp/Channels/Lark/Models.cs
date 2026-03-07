using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Lark;

// ── Webhook Event DTOs ───────────────────────────────────────────────

/// <summary>
/// Top-level webhook event envelope from Feishu/Lark (schema 2.0).
/// Also handles <c>url_verification</c> challenges (the <see cref="Challenge"/> field is populated).
/// </summary>
internal sealed class LarkWebhookEvent
{
    /// <summary>Schema version (always "2.0" for current events).</summary>
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    /// <summary>Event header (present on real events, absent on url_verification).</summary>
    [JsonPropertyName("header")]
    public LarkEventHeader? Header { get; init; }

    /// <summary>Event body (present on real events).</summary>
    [JsonPropertyName("event")]
    public LarkMessageEvent? Event { get; init; }

    /// <summary>Challenge string for url_verification handshake.</summary>
    [JsonPropertyName("challenge")]
    public string? Challenge { get; init; }

    /// <summary>Event type for url_verification ("url_verification").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Verification token echoed in url_verification challenges.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    /// <summary>Encryption key for encrypted events (not used in plain mode).</summary>
    [JsonPropertyName("encrypt")]
    public string? Encrypt { get; init; }
}

/// <summary>Event header containing event type, tenant key, and app ID.</summary>
internal sealed class LarkEventHeader
{
    [JsonPropertyName("event_id")]
    public string? EventId { get; init; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }

    [JsonPropertyName("tenant_key")]
    public string? TenantKey { get; init; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>Event body for <c>im.message.receive_v1</c>.</summary>
internal sealed class LarkMessageEvent
{
    [JsonPropertyName("sender")]
    public LarkSender? Sender { get; init; }

    [JsonPropertyName("message")]
    public LarkMessage? Message { get; init; }
}

/// <summary>Sender info within a Lark event.</summary>
internal sealed class LarkSender
{
    [JsonPropertyName("sender_id")]
    public LarkSenderId? SenderId { get; init; }

    [JsonPropertyName("sender_type")]
    public string? SenderType { get; init; }
}

/// <summary>Sender ID with open_id, user_id, and union_id.</summary>
internal sealed class LarkSenderId
{
    [JsonPropertyName("open_id")]
    public string? OpenId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("union_id")]
    public string? UnionId { get; init; }
}

/// <summary>Message content within a Lark event.</summary>
internal sealed class LarkMessage
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }

    [JsonPropertyName("root_id")]
    public string? RootId { get; init; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; init; }

    [JsonPropertyName("chat_id")]
    public string? ChatId { get; init; }

    [JsonPropertyName("chat_type")]
    public string? ChatType { get; init; }

    [JsonPropertyName("message_type")]
    public string? MessageType { get; init; }

    /// <summary>
    /// Double-encoded JSON string. For text messages this is <c>"{\"text\":\"hello\"}"</c>.
    /// Must be parsed again with <see cref="System.Text.Json.JsonDocument"/> to extract the text.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

// ── Auth DTOs ────────────────────────────────────────────────────────

/// <summary>Request body for tenant_access_token/internal API.</summary>
internal sealed class LarkTokenRequest
{
    [JsonPropertyName("app_id")]
    public string AppId { get; init; } = "";

    [JsonPropertyName("app_secret")]
    public string AppSecret { get; init; } = "";
}

/// <summary>Response from tenant_access_token/internal API.</summary>
internal sealed class LarkTokenResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }

    [JsonPropertyName("tenant_access_token")]
    public string? TenantAccessToken { get; init; }

    [JsonPropertyName("expire")]
    public int Expire { get; init; }
}

// ── Send DTOs ────────────────────────────────────────────────────────

/// <summary>Request body for sending a message via <c>im/v1/messages</c>.</summary>
internal sealed class LarkSendMessageRequest
{
    [JsonPropertyName("receive_id")]
    public string ReceiveId { get; init; } = "";

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("msg_type")]
    public string MsgType { get; init; } = "text";
}

/// <summary>Response from send message API.</summary>
internal sealed class LarkSendMessageResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("msg")]
    public string? Msg { get; init; }
}

// ── Challenge echo response ──────────────────────────────────────────

/// <summary>Response body for the url_verification challenge handshake.</summary>
internal sealed class LarkChallengeResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; init; } = "";
}

/// <summary>Inner text content extracted from the double-encoded message content field.</summary>
internal sealed class LarkTextContent
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

// ── JSON Context ─────────────────────────────────────────────────────

[JsonSerializable(typeof(LarkWebhookEvent))]
[JsonSerializable(typeof(LarkTokenRequest))]
[JsonSerializable(typeof(LarkTokenResponse))]
[JsonSerializable(typeof(LarkSendMessageRequest))]
[JsonSerializable(typeof(LarkSendMessageResponse))]
[JsonSerializable(typeof(LarkChallengeResponse))]
[JsonSerializable(typeof(LarkTextContent))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class LarkJsonContext : JsonSerializerContext;