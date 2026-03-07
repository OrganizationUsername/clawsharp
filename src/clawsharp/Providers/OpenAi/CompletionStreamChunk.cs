using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A single SSE chunk from the OpenAI streaming chat completions API.</summary>
internal sealed class CompletionStreamChunk
{
    /// <summary>List of streaming choices.</summary>
    [JsonPropertyName("choices")]
    public List<StreamChoice>? Choices { get; init; }

    /// <summary>Token usage data, present on the final chunk when <c>stream_options.include_usage</c> is set.</summary>
    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }
}