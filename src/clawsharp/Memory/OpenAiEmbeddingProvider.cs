using System.Net.Http.Json;

namespace Clawsharp.Memory;

/// <summary>
///     Embedding provider using the OpenAI-compatible /v1/embeddings endpoint.
///     Works with OpenAI, Azure OpenAI, OpenRouter, and any compatible API.
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly string _apiKey;

    private readonly string _model;

    private readonly string _baseUrl;

    public OpenAiEmbeddingProvider(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string model = "text-embedding-3-small",
        string baseUrl = "https://api.openai.com/v1")
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = apiKey;
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public int Dimensions => _model switch
    {
        "text-embedding-3-small" => 1536,
        "text-embedding-3-large" => 3072,
        _ => 768
    };

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("llm");
        var request = new EmbeddingRequest { Model = _model, Input = text };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/embeddings");
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
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