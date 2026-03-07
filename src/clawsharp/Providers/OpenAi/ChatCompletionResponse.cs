using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>Response from the OpenAI Chat Completions API.</summary>
internal sealed class ChatCompletionResponse
{
    /// <summary>List of completion choices (typically one).</summary>
    [JsonPropertyName("choices")]
    public List<CompletionChoice> Choices { get; init; } = [];

    /// <summary>Token usage statistics for this request.</summary>
    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }
}