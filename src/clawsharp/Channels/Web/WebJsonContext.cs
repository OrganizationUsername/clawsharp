using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Web;

/// <summary>Source-generated JSON serialization context for Web channel models.</summary>
[JsonSerializable(typeof(WebAuthResponse))]
[JsonSerializable(typeof(WebChatRequest))]
[JsonSerializable(typeof(WebChatResponse))]
[JsonSerializable(typeof(WebPairResponse))]
[JsonSerializable(typeof(WebStreamDelta))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class WebJsonContext : JsonSerializerContext;