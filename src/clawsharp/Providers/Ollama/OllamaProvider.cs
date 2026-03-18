using Clawsharp.Core;
using Clawsharp.Core.Utilities;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.Ollama;

/// <summary>
///     Thin wrapper: delegates to OpenAiProvider pointed at Ollama's OpenAI-compatible endpoint.
///     Inherits streaming support via <see cref="IStreamingProvider" /> from the inner provider.
/// </summary>
public sealed class OllamaProvider(IHttpClientFactory httpClientFactory, string baseUrl = ClawsharpConstants.OllamaDefaultBaseUrl)
    : IProvider, IStreamingProvider
{
    private readonly OpenAiProvider _inner = new(httpClientFactory, baseUrl.TrimEnd('/') + "/v1", "", "ollama");

    public string Name => "ollama";

    /// <inheritdoc />
    public bool SupportsVision => true;

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        return _inner.ChatAsync(request, ct);
    }

    public IAsyncEnumerable<StreamChunk> StreamAsync(ChatRequest request, CancellationToken ct = default)
    {
        return _inner.StreamAsync(request, ct);
    }
}