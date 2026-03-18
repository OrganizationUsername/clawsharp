using Intellenum;

namespace Clawsharp.Config;

/// <summary>Well-known embedding provider identifiers.</summary>
[Intellenum<string>]
internal partial class EmbeddingProvider
{
    public static readonly EmbeddingProvider None = new("none");
    public static readonly EmbeddingProvider OpenAi = new("openai");
    public static readonly EmbeddingProvider Ollama = new("ollama");
}
