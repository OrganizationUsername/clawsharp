using System.Net.Http.Json;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Memory;

/// <summary>
///     Embedding provider using the Ollama /api/embeddings endpoint.
/// </summary>
public sealed class OllamaEmbeddingProvider(
    IHttpClientFactory httpClientFactory,
    string model = "nomic-embed-text",
    string baseUrl = ClawsharpConstants.OllamaDefaultBaseUrl,
    int dimensions = 768)
    : IEmbeddingProvider
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public int Dimensions => dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("llm");
        var request = new OllamaEmbeddingRequest { Model = model, Prompt = text };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/embeddings");
        httpRequest.Content = JsonContent.Create(request, EmbeddingJsonContext.Default.OllamaEmbeddingRequest);

        using var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(EmbeddingJsonContext.Default.OllamaEmbeddingResponse, ct);
        var embedding = result?.Embedding;

        if (embedding is null || embedding.Length == 0)
        {
            throw new InvalidOperationException("Embedding provider returned null/empty embedding");
        }

        if (embedding.Length != Dimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: expected {Dimensions}, got {embedding.Length}");
        }

        return embedding;
    }
}