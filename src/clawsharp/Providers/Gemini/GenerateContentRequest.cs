using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clawsharp.Core;

namespace Clawsharp.Providers.Gemini;

/// <summary>Request body for the Gemini generateContent API.</summary>
internal sealed class GenerateContentRequest : IRequest<GenerateContentRequest, GenerateContentResponse>
{
    /// <summary>Conversation contents.</summary>
    [JsonPropertyName("contents")]
    public List<ContentItem> Contents { get; init; } = [];

    /// <summary>System instruction content.</summary>
    [JsonPropertyName("systemInstruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentItem? SystemInstruction { get; init; }

    /// <summary>Available tools (function declarations).</summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; init; }

    /// <summary>Generation configuration (temperature, max tokens).</summary>
    [JsonPropertyName("generationConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GenerationConfig? GenerationConfig { get; init; }

    /// <summary>Thinking configuration controlling the model's internal reasoning budget.</summary>
    [JsonPropertyName("thinkingConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiThinkingConfig? ThinkingConfig { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public string Url { get; init; } = "";

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<GenerateContentRequest> RequestTypeInfo => GeminiJsonContext.Default.GenerateContentRequest;

    /// <inheritdoc />
    [JsonIgnore]
    public JsonTypeInfo<GenerateContentResponse> ResponseTypeInfo => GeminiJsonContext.Default.GenerateContentResponse;
}