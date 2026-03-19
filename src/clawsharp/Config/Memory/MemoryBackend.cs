using Intellenum;

namespace Clawsharp.Config.Memory;

/// <summary>Supported memory storage backend identifiers.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class MemoryBackend
{
    /// <summary>File-based Markdown memory (MEMORY.md + HISTORY.md).</summary>
    public static readonly MemoryBackend Markdown = new("markdown");

    /// <summary>SQLite with FTS5 full-text search.</summary>
    public static readonly MemoryBackend Sqlite = new("sqlite");

    /// <summary>PostgreSQL with tsvector GIN index.</summary>
    public static readonly MemoryBackend Postgres = new("postgres");

    /// <summary>Microsoft SQL Server with optional CONTAINS.</summary>
    public static readonly MemoryBackend MsSql = new("mssql");

    /// <summary>Redis with RediSearch for full-text and vector search.</summary>
    public static readonly MemoryBackend Redis = new("redis");
}