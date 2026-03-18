using System.Net.Http.Json;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Memory;

/// <summary>
///     Embedding provider using the OpenAI-compatible /v1/embeddings endpoint.
///     Works with OpenAI, Azure OpenAI, OpenRouter, and any compatible API.
/// </summary>
public sealed class OpenAiEmbeddingProvider(
    IHttpClientFactory httpClientFactory,
    string apiKey,
    string model = "text-embedding-3-small",
    string baseUrl = ClawsharpConstants.OpenAiDefaultBaseUrl)
    : IEmbeddingProvider
{
    private const int SmallModelDimensions = 1536;
    private const int LargeModelDimensions = 3072;
    private const int DefaultModelDimensions = 768;

    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public int Dimensions => model switch
    {
        "text-embedding-3-small" => SmallModelDimensions,
        "text-embedding-3-large" => LargeModelDimensions,
        _ => DefaultModelDimensions
    };

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("llm");
        var request = new EmbeddingRequest { Model = model, Input = text };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/embeddings");
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(request, EmbeddingJsonContext.Default.EmbeddingRequest);

        using var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(EmbeddingJsonContext.Default.EmbeddingResponse, ct);
        var embedding = result?.Data is { Length: > 0 } ? result.Data[0].Embedding : null;

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