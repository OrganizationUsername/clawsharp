using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Qq;

[JsonSerializable(typeof(QqSendPrivateMessage))]
[JsonSerializable(typeof(QqSendGroupMessage))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class QqJsonContext : JsonSerializerContext;

internal sealed record QqSendPrivateMessage(
    [property: JsonPropertyName("user_id")]
    string UserId,
    [property: JsonPropertyName("message")]
    string Message);

internal sealed record QqSendGroupMessage(
    [property: JsonPropertyName("group_id")]
    string GroupId,
    [property: JsonPropertyName("message")]
    string Message);