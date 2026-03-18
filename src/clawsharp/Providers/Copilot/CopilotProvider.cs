using System.Runtime.CompilerServices;
using Clawsharp.Auth;
using Clawsharp.Core;
using Clawsharp.Providers.OpenAi;
using Spectre.Console;

namespace Clawsharp.Providers.Copilot;

/// <summary>
/// GitHub Copilot LLM provider. Uses the OpenAI-compatible Copilot API at
/// https://api.githubcopilot.com. Authenticates via OAuth tokens stored by
/// <see cref="AuthStore"/> and auto-refreshes expired Copilot tokens.
///
/// Delegates to <see cref="OpenAiProvider"/> via composition (matching the
/// Ollama/LmStudio pattern) to avoid duplicating request building, response
/// parsing, and streaming logic. Gains <see cref="IStreamingProvider"/> for free.
/// </summary>
public sealed class CopilotProvider(IHttpClientFactory httpClientFactory, GitHubDeviceFlow deviceFlow) : IStreamingProvider, IDisposable
{
    private const string CopilotBaseUrl = "https://api.githubcopilot.com";

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public string Name => "copilot";

    /// <inheritdoc />
    public bool SupportsVision => true;

    /// <inheritdoc />
    public void Dispose() => _refreshLock.Dispose();

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var inner = await CreateInnerProviderAsync(ct);
        return await inner.ChatAsync(request, ct);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var inner = await CreateInnerProviderAsync(ct);
        await foreach (var chunk in inner.StreamAsync(request, ct))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Creates an <see cref="OpenAiProvider"/> pointed at the Copilot base URL with the
    /// current (possibly refreshed) Copilot auth token. The provider is lightweight and
    /// holds no mutable state, so creating one per request is inexpensive.
    /// </summary>
    /// <remarks>
    /// The original CopilotProvider sent an "editor-version: clawsharp/1.0" header.
    /// This header is advisory telemetry for GitHub and is not required for authentication.
    /// It is not forwarded through OpenAiProvider, which only sets the Authorization header.
    /// </remarks>
    private async Task<OpenAiProvider> CreateInnerProviderAsync(CancellationToken ct)
    {
        var token = await GetAuthTokenAsync(ct);
        return new OpenAiProvider(httpClientFactory, CopilotBaseUrl, token, "copilot");
    }

    private async Task<string> GetAuthTokenAsync(CancellationToken ct)
    {
        var oauthToken = await AuthStore.LoadAsync("copilot", ct);
        if (oauthToken is null)
        {
            throw new InvalidOperationException(
                "Not logged in to GitHub Copilot. Run: clawsharp auth login-copilot");
        }

        if (!oauthToken.IsExpired)
        {
            return oauthToken.AccessToken;
        }

        // Token expired -- refresh using stored GitHub OAuth token
        await _refreshLock.WaitAsync(ct);
        try
        {
            oauthToken = await AuthStore.LoadAsync("copilot", ct);
            if (oauthToken is not null && !oauthToken.IsExpired)
            {
                return oauthToken.AccessToken;
            }

            if (oauthToken?.RefreshToken is null)
            {
                throw new InvalidOperationException(
                    "Copilot token expired and no GitHub token available for refresh. Run: clawsharp auth login-copilot");
            }

            AnsiConsole.MarkupLine("[yellow][[copilot]][/] Token expired, refreshing...");
            var refreshed = await deviceFlow.RefreshCopilotTokenAsync(oauthToken.RefreshToken, ct);
            if (refreshed is null)
            {
                throw new InvalidOperationException(
                    "Failed to refresh Copilot token. Run: clawsharp auth login-copilot");
            }

            await AuthStore.SaveAsync("copilot", refreshed, ct);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}