using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>Source-generated JSON serialization context for Gemini API models.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(GenerateContentRequest))]
[JsonSerializable(typeof(GenerateContentResponse))]
[JsonSerializable(typeof(UsageMetadata))]
[JsonSerializable(typeof(ContentItem))]
[JsonSerializable(typeof(ContentPart))]
[JsonSerializable(typeof(FunctionCall))]
[JsonSerializable(typeof(FunctionResponse))]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(FunctionDeclaration))]
[JsonSerializable(typeof(GenerationConfig))]
[JsonSerializable(typeof(GeminiThinkingConfig))]
[JsonSerializable(typeof(ContentCandidate))]
[JsonSerializable(typeof(GeminiApiError))]
[JsonSerializable(typeof(GeminiInlineData))]
[JsonSerializable(typeof(List<ContentItem>))]
[JsonSerializable(typeof(List<ContentPart>))]
[JsonSerializable(typeof(List<ToolDefinition>))]
[JsonSerializable(typeof(List<FunctionDeclaration>))]
[JsonSerializable(typeof(List<ContentCandidate>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class GeminiJsonContext : JsonSerializerContext;