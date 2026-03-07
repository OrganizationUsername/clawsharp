using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Source-generated JSON serialization context for OpenAI API models.</summary>
[JsonSerializable(typeof(PromptTokensDetails))]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(CompletionMessage))]
[JsonSerializable(typeof(MessageContent))]
[JsonSerializable(typeof(ContentPart))]
[JsonSerializable(typeof(ImageUrl))]
[JsonSerializable(typeof(List<ContentPart>))]
[JsonSerializable(typeof(FunctionTool))]
[JsonSerializable(typeof(FunctionDefinition))]
[JsonSerializable(typeof(FunctionToolCall))]
[JsonSerializable(typeof(FunctionToolCallDetail))]
[JsonSerializable(typeof(CompletionChoice))]
[JsonSerializable(typeof(List<CompletionMessage>))]
[JsonSerializable(typeof(List<FunctionTool>))]
[JsonSerializable(typeof(List<CompletionChoice>))]
[JsonSerializable(typeof(List<FunctionToolCall>))]
[JsonSerializable(typeof(CompletionStreamChunk))]
[JsonSerializable(typeof(StreamChoice))]
[JsonSerializable(typeof(StreamDelta))]
[JsonSerializable(typeof(StreamToolCall))]
[JsonSerializable(typeof(StreamFunction))]
[JsonSerializable(typeof(List<StreamChoice>))]
[JsonSerializable(typeof(List<StreamToolCall>))]
[JsonSerializable(typeof(StreamOptions))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class OpenAiJsonContext : JsonSerializerContext;