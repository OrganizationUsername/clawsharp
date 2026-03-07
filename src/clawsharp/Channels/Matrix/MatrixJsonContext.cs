using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Matrix;

/// <summary>Source-generated JSON serialization context for Matrix channel models.</summary>
[JsonSerializable(typeof(MatrixLoginRequest))]
[JsonSerializable(typeof(MatrixLoginResponse))]
[JsonSerializable(typeof(MatrixSyncResponse))]
[JsonSerializable(typeof(MatrixSendMessageRequest))]
[JsonSerializable(typeof(MatrixSendMessageResponse))]
[JsonSerializable(typeof(MatrixEditMessageRequest))]
[JsonSerializable(typeof(MatrixNewContent))]
[JsonSerializable(typeof(MatrixRelatesTo))]
[JsonSerializable(typeof(Dictionary<string, MatrixJoinedRoom>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class MatrixJsonContext : JsonSerializerContext;