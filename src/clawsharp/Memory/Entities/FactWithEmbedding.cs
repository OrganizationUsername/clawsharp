namespace Clawsharp.Memory.Entities;

/// <summary>Projection DTO for loading facts with their embedding JSON from raw SQL.</summary>
internal sealed class FactWithEmbedding
{
    public long Id { get; set; }

    public string Content { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public int AccessCount { get; set; }

    public DateTimeOffset? LastAccessedAt { get; set; }

    public string? EmbeddingJson { get; set; }
}