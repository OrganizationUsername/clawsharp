using System.Text.Json.Serialization;

namespace Clawsharp.Memory;

// ── OpenAI Embeddings API DTOs ───────────────────────────────────────

public sealed class EmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("input")]
    public string Input { get; init; } = "";
}

public sealed class EmbeddingResponse
{
    [JsonPropertyName("data")]
    public EmbeddingData[] Data { get; init; } = [];
}

public sealed class EmbeddingData
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; } = [];
}

// ── Ollama Embeddings API DTOs ───────────────────────────────────────

public sealed class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";
}

public sealed class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; } = [];
}

// ── Source-generated JSON context ────────────────────────────────────

[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
[JsonSerializable(typeof(EmbeddingData))]
[JsonSerializable(typeof(OllamaEmbeddingRequest))]
[JsonSerializable(typeof(OllamaEmbeddingResponse))]
[JsonSerializable(typeof(float[]))]
internal sealed partial class EmbeddingJsonContext : JsonSerializerContext;