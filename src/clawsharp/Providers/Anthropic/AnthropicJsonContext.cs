using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Anthropic;

/// <summary>Source-generated JSON serialization context for Anthropic API models.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(MessagesRequest))]
[JsonSerializable(typeof(MessagesResponse))]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(ConversationMessage))]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(TextBlock))]
[JsonSerializable(typeof(ToolUseBlock))]
[JsonSerializable(typeof(ToolResultBlock))]
[JsonSerializable(typeof(ImageBlock))]
[JsonSerializable(typeof(ImageSource))]
[JsonSerializable(typeof(CacheControl))]
[JsonSerializable(typeof(AnthropicThinking))]
[JsonSerializable(typeof(AnthropicSystemBlock))]
[JsonSerializable(typeof(List<AnthropicSystemBlock>))]
[JsonSerializable(typeof(List<ConversationMessage>))]
[JsonSerializable(typeof(List<ToolDefinition>))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(List<TextBlock>))]
[JsonSerializable(typeof(List<ToolResultBlock>))]
[JsonSerializable(typeof(StreamEvent))]
[JsonSerializable(typeof(StreamDelta))]
[JsonSerializable(typeof(StreamContentBlock))]
[JsonSerializable(typeof(StreamMessageStart))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AnthropicJsonContext : JsonSerializerContext;