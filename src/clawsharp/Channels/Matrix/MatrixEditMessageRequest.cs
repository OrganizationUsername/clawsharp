using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Matrix;

/// <summary>
/// Request body for editing a Matrix message via m.replace relation.
/// Sent as PUT /rooms/{roomId}/send/m.room.message/{txnId}.
/// </summary>
internal sealed class MatrixEditMessageRequest : IRequest<MatrixEditMessageRequest, MatrixSendMessageResponse>
{
    /// <summary>Message type (always "m.text" for text messages).</summary>
    [JsonPropertyName("msgtype")]
    public string MsgType { get; init; } = "m.text";

    /// <summary>Fallback body with edit marker (e.g. "* edited text").</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";

    /// <summary>The new content for the edited message.</summary>
    [JsonPropertyName("m.new_content")]
    public MatrixNewContent? NewContent { get; init; }

    /// <summary>Relation metadata pointing to the original event being replaced.</summary>
    [JsonPropertyName("m.relates_to")]
    public MatrixRelatesTo? RelatesTo { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MatrixEditMessageRequest> RequestTypeInfo => MatrixJsonContext.Default.MatrixEditMessageRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MatrixSendMessageResponse> ResponseTypeInfo => MatrixJsonContext.Default.MatrixSendMessageResponse;
}

/// <summary>The replacement content for a Matrix message edit.</summary>
internal sealed class MatrixNewContent
{
    /// <summary>Message type.</summary>
    [JsonPropertyName("msgtype")]
    public string MsgType { get; init; } = "m.text";

    /// <summary>The new message body text.</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";
}

/// <summary>Relation metadata for Matrix message edits (m.replace).</summary>
internal sealed class MatrixRelatesTo
{
    /// <summary>The relation type (always "m.replace" for edits).</summary>
    [JsonPropertyName("rel_type")]
    public string RelType { get; init; } = "m.replace";

    /// <summary>The event ID of the original message being replaced.</summary>
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = "";
}