using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Providers;

public interface IProvider
{
    string Name { get; }

    /// <summary>Whether this provider supports vision/image inputs.</summary>
    bool SupportsVision => false;

    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default);
}

/// <summary>
///     Optional extension of <see cref="IProvider" /> that supports token-by-token streaming.
///     Providers that implement this interface can stream responses as they are generated,
///     which reduces time-to-first-token and enables live display on supporting channels.
/// </summary>
public interface IStreamingProvider : IProvider
{
    IAsyncEnumerable<StreamChunk> StreamAsync(ChatRequest request, CancellationToken ct = default);
}