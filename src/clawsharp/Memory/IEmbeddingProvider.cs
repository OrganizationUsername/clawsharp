namespace Clawsharp.Memory;

/// <summary>Generates dense vector embeddings for text.</summary>
public interface IEmbeddingProvider
{
    /// <summary>Returns the embedding dimension (e.g. 1536 for text-embedding-3-small).</summary>
    int Dimensions { get; }

    /// <summary>Generate a normalized embedding vector for the given text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}