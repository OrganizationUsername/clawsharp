using System.Text.Json;
using System.Text.Json.Serialization;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Sessions;

[JsonSerializable(typeof(Session)), JsonSerializable(typeof(List<ChatMessage>)), JsonSerializable(typeof(ChatMessage)),
 JsonSerializable(typeof(ToolCall)), JsonSerializable(typeof(MessageRole)),
 JsonSerializable(typeof(ImageAttachment)), JsonSerializable(typeof(IReadOnlyList<ImageAttachment>)),
 JsonSerializable(typeof(IReadOnlyList<ToolCall>)), JsonSerializable(typeof(DateTimeOffset?)),
 JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, Converters = [typeof(MessageRoleJsonConverter)])]
internal partial class SessionJsonContext : JsonSerializerContext;