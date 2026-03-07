namespace Clawsharp.Config.Memory;

/// <summary>Configuration for the embedding provider used by semantic memory search.</summary>
public sealed class EmbeddingConfig
{
    /// <summary>Embedding provider: "openai" | "ollama" | "none".</summary>
    public string Provider { get; init; } = "none";

    /// <summary>API key for the embedding provider (required for OpenAI).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Embedding model name (e.g. "text-embedding-3-small", "nomic-embed-text").</summary>
    public string? Model { get; init; }

    /// <summary>Base URL override for the embedding API.</summary>
    public string? BaseUrl { get; init; }
}