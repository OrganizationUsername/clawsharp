using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Signal;

/// <summary>
/// Top-level payload delivered via SSE on GET /api/v1/events (signal-cli).
/// Each event has an "account" field (the registered phone) and an "envelope".
/// </summary>
internal sealed class SignalSseEvent
{
    [JsonPropertyName("account")]
    public string? Account { get; init; }

    [JsonPropertyName("envelope")]
    public SignalEnvelopeContent? Envelope { get; init; }
}

internal sealed class SignalEnvelopeContent
{
    /// <summary>Legacy identifier — prefer <see cref="SourceNumber"/> or <see cref="SourceUuid"/>.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("sourceNumber")]
    public string? SourceNumber { get; init; }

    [JsonPropertyName("sourceUuid")]
    public string? SourceUuid { get; init; }

    [JsonPropertyName("sourceName")]
    public string? SourceName { get; init; }

    [JsonPropertyName("sourceDevice")]
    public int? SourceDevice { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("serverReceivedTimestamp")]
    public long ServerReceivedTimestamp { get; init; }

    [JsonPropertyName("serverDeliveredTimestamp")]
    public long ServerDeliveredTimestamp { get; init; }

    [JsonPropertyName("dataMessage")]
    public SignalDataMessage? DataMessage { get; init; }
}

internal sealed class SignalDataMessage
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; init; }

    [JsonPropertyName("attachments")]
    public List<SignalAttachment>? Attachments { get; init; }
}

/// <summary>
/// Attachment metadata from signal-cli. Files are stored locally on the
/// signal-cli host; no download URL is exposed via the HTTP API.
/// </summary>
internal sealed class SignalAttachment
{
    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }

    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    /// <summary>Local attachment ID assigned by signal-cli.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("size")]
    public long? Size { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("uploadTimestamp")]
    public long? UploadTimestamp { get; init; }
}

// ── JSON-RPC 2.0 — send ───────────────────────────────────────────────────

/// <summary>
/// JSON-RPC 2.0 request for POST /api/v1/rpc — method "send".
/// </summary>
internal sealed class SignalSendRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; } = "send";

    [JsonPropertyName("params")]
    public SignalSendParams? Params { get; init; }
}

internal sealed class SignalSendParams
{
    [JsonPropertyName("account")]
    public string Account { get; init; } = "";

    [JsonPropertyName("recipients")]
    public List<string> Recipients { get; init; } = [];

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

// ── JSON-RPC 2.0 — getAttachment ─────────────────────────────────────────

/// <summary>
/// JSON-RPC 2.0 request for POST /api/v1/rpc — method "getAttachment".
/// Returns the stored attachment file base64-encoded.
/// </summary>
internal sealed class SignalGetAttachmentRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; } = "getAttachment";

    [JsonPropertyName("params")]
    public SignalGetAttachmentParams? Params { get; init; }
}

internal sealed class SignalGetAttachmentParams
{
    [JsonPropertyName("account")]
    public string Account { get; init; } = "";

    /// <summary>Attachment ID from <see cref="SignalAttachment.Id"/>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Phone number of the sender (required unless GroupId is set).</summary>
    [JsonPropertyName("recipient")]
    public string? Recipient { get; init; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 response for "getAttachment" — result carries base64 data.
/// </summary>
internal sealed class SignalGetAttachmentResponse
{
    [JsonPropertyName("result")]
    public SignalAttachmentData? Result { get; init; }

    [JsonPropertyName("error")]
    public SignalJsonRpcError? Error { get; init; }
}

internal sealed class SignalAttachmentData
{
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

// ── JSON-RPC 2.0 — shared ────────────────────────────────────────────────

/// <summary>Minimal JSON-RPC 2.0 response — checked for error after send.</summary>
internal sealed class SignalJsonRpcResponse
{
    [JsonPropertyName("error")]
    public SignalJsonRpcError? Error { get; init; }
}

internal sealed class SignalJsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

[JsonSerializable(typeof(SignalSseEvent))]
[JsonSerializable(typeof(SignalSendRpcRequest))]
[JsonSerializable(typeof(SignalGetAttachmentRpcRequest))]
[JsonSerializable(typeof(SignalGetAttachmentResponse))]
[JsonSerializable(typeof(SignalJsonRpcResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class SignalJsonContext : JsonSerializerContext;