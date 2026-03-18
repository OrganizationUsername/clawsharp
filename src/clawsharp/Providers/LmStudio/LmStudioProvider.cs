using Clawsharp.Core;
using Clawsharp.Core.Utilities;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.LmStudio;

/// <summary>
///     Thin wrapper: delegates to OpenAiProvider pointed at LM Studio's OpenAI-compatible endpoint.
///     Inherits streaming support via <see cref="IStreamingProvider" /> from the inner provider.
/// </summary>
public sealed class LmStudioProvider(IHttpClientFactory httpClientFactory, string baseUrl = ClawsharpConstants.LmStudioDefaultBaseUrl)
    : IProvider, IStreamingProvider
{
    private readonly OpenAiProvider _inner = new(httpClientFactory, baseUrl.TrimEnd('/') + "/v1", "", "lmstudio");

    public string Name => "lmstudio";

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