using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clawsharp.Config.Memory;

/// <summary>Memory backend configuration.</summary>
public sealed class MemoryConfig
{
    /// <summary>
    /// Backend to use: "markdown" | "sqlite" | "postgres" | "mssql" | "redis".
    /// </summary>
    [Required]
    public string Backend { get; init; } = "markdown";

    /// <summary>Parsed <see cref="MemoryBackend"/> value derived from <see cref="Backend"/>.</summary>
    [JsonIgnore]
    public MemoryBackend BackendType
    {
        get => MemoryBackend.TryFromValue(Backend, out var b) ? b : MemoryBackend.Markdown;
        // No-op setter: required for the Configuration Binding Source Generator (EnableConfigurationBindingGenerator).
        // The generator emits assignment code for all public settable properties; value is ignored at runtime
        // because BackendType is always derived from the Backend string property.
        internal set { }
    }

    /// <summary>
    /// Local directory for file-based backends (markdown, sqlite).
    /// </summary>
    [Required]
    public string Dir { get; init; } = "~/.clawsharp/memory";

    /// <summary>
    /// ADO.NET connection string for database backends (postgres, mssql).
    /// Supports ${ENV_VAR} expansion.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Embedding provider configuration for semantic / vector memory search.
    /// When configured, facts are embedded at write time and hybrid search is available.
    /// </summary>
    public EmbeddingConfig? Embedding { get; init; }

    /// <summary>
    /// Memory decay configuration for automatic TTL-based pruning and soft relevance decay.
    /// </summary>
    public MemoryDecayConfig? Decay { get; init; }

    /// <summary>
    /// Embedding vector dimension. Must match the embedding model output dimension.
    /// Common values: 1536 (text-embedding-3-small), 3072 (text-embedding-3-large), 768 (nomic-embed-text).
    /// Used to create sqlite-vec virtual tables and pgvector columns/indexes.
    /// </summary>
    [Range(1, 65536)]
    public int EmbeddingDimension { get; init; } = 1536;

    /// <summary>
    /// Enhanced recall configuration. When enabled, keywords are extracted from user messages
    /// and secondary memory searches are performed, merging results with the primary recall.
    /// </summary>
    public EnhancedRecallConfig? EnhancedRecall { get; init; }

    /// <summary>
    /// Post-turn fact extraction configuration. When enabled, conversation turns are accumulated
    /// and periodically submitted to the LLM for durable fact extraction into memory.
    /// </summary>
    public FactExtractionConfig? FactExtraction { get; init; }
}

/// <summary>Configuration for enhanced recall with keyword expansion.</summary>
public sealed class EnhancedRecallConfig
{
    /// <summary>Enable enhanced recall with keyword expansion. Default: true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Maximum number of keywords to search per message. Default: 5.</summary>
    [Range(1, 20)]
    public int MaxKeywords { get; init; } = 5;

    /// <summary>Maximum results per keyword search. Default: 3.</summary>
    [Range(1, 20)]
    public int ResultsPerKeyword { get; init; } = 3;
}

/// <summary>Configuration for post-turn durable fact extraction.</summary>
public sealed class FactExtractionConfig
{
    /// <summary>Enable post-turn fact extraction. Default: true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Minimum number of accumulated turns before triggering extraction. Default: 5.</summary>
    [Range(1, 50)]
    public int MinTurns { get; init; } = 5;

    /// <summary>Minimum total character count before triggering extraction. Default: 200.</summary>
    [Range(50, 10000)]
    public int MinChars { get; init; } = 200;
}