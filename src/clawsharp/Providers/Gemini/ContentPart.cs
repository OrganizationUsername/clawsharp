using System.Text.Json.Serialization;

namespace Clawsharp.Providers.Gemini;

/// <summary>A single part within a Gemini content item (text, functionCall, or functionResponse).</summary>
internal sealed class ContentPart
{
    /// <summary>Text content.</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>Function call data (set by the model).</summary>
    [JsonPropertyName("functionCall")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FunctionCall? FunctionCall { get; init; }

    /// <summary>Function response data (set by the client).</summary>
    [JsonPropertyName("functionResponse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FunctionResponse? FunctionResponse { get; init; }

    /// <summary>Inline binary data for image/audio content.</summary>
    [JsonPropertyName("inline_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiInlineData? InlineData { get; init; }
}