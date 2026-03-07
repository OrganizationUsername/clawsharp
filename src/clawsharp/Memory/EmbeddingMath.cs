using System.Numerics.Tensors;
using System.Text.Json;

namespace Clawsharp.Memory;

/// <summary>
///     Static helpers for cosine similarity and embedding serialization.
/// </summary>
internal static class EmbeddingMath
{
    /// <summary>Compute cosine similarity between two vectors using hardware-accelerated TensorPrimitives.</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Vector dimension mismatch: {a.Length} vs {b.Length}");
        }

        var sim = TensorPrimitives.CosineSimilarity((ReadOnlySpan<float>)a, (ReadOnlySpan<float>)b);
        return float.IsNaN(sim) ? 0f : sim;
    }

    /// <summary>Deserialize a JSON-encoded float array, or return empty if null.</summary>
    public static float[] Deserialize(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize(json, EmbeddingJsonContext.Default.SingleArray) ?? [];

    /// <summary>Serialize a float array to JSON.</summary>
    public static string Serialize(float[] embedding) =>
        JsonSerializer.Serialize(embedding, EmbeddingJsonContext.Default.SingleArray);
}