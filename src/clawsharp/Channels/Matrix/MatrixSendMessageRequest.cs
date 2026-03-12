using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Channels.Matrix;

/// <summary>
/// Request body for sending a message to a Matrix room via PUT /rooms/{roomId}/send/m.room.message/{txnId}.
/// </summary>
internal sealed class MatrixSendMessageRequest : IRequest<MatrixSendMessageRequest, MatrixSendMessageResponse>
{
    /// <summary>Message type (always "m.text" for text messages).</summary>
    [JsonPropertyName("msgtype")]
    public string MsgType { get; init; } = "m.text";

    /// <summary>The message body text.</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MatrixSendMessageRequest> RequestTypeInfo => MatrixJsonContext.Default.MatrixSendMessageRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<MatrixSendMessageResponse> ResponseTypeInfo => MatrixJsonContext.Default.MatrixSendMessageResponse;
}