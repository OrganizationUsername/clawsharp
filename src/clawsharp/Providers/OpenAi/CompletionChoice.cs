using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A single choice within an OpenAI chat completion response.</summary>
internal sealed class CompletionChoice
{
    /// <summary>The assistant's message for this choice.</summary>
    [JsonPropertyName("message")]
    public CompletionMessage Message { get; init; } = new();

    /// <summary>Reason the model stopped generating ("stop", "tool_calls", "length").</summary>
    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; init; } = "stop";
}