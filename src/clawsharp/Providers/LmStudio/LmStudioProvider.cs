using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Providers.LmStudio;

/// <summary>
///     Thin wrapper: delegates to OpenAiProvider pointed at LM Studio's OpenAI-compatible endpoint.
///     Inherits streaming support via <see cref="IStreamingProvider" /> from the inner provider.
/// </summary>
public sealed class LmStudioProvider : IProvider, IStreamingProvider
{
    private readonly OpenAiProvider _inner;

    public LmStudioProvider(IHttpClientFactory httpClientFactory, string baseUrl = "http://localhost:1234")
    {
        _inner = new OpenAiProvider(httpClientFactory, baseUrl.TrimEnd('/') + "/v1", "", "lmstudio");
    }

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