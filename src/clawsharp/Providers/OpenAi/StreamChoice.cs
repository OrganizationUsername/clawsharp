using System.Text.Json.Serialization;

namespace Clawsharp.Providers.OpenAi;

/// <summary>A single choice within a streaming chat completion chunk.</summary>
internal sealed class StreamChoice
{
    /// <summary>The delta content for this streaming choice.</summary>
    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; init; }
}