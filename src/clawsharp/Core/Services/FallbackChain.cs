using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Clawsharp.Providers;
using Microsoft.Extensions.Logging;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Services;

/// <summary>
///     Orchestrates provider call execution with fallback across candidates.
///     Skips providers in cooldown, classifies errors, and records cooldowns on failure.
/// </summary>
public sealed partial class FallbackChain(CooldownTracker cooldowns, ILogger<FallbackChain> logger)
{
    /// <summary>
    ///     Execute a provider call with fallback across candidates.
    ///     Tries each candidate in order, skipping those in cooldown.
    ///     Non-retriable errors (Format) abort immediately without fallback.
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        IReadOnlyList<(string Name, IProvider Provider)> candidates,
        Func<string, IProvider, CancellationToken, Task<TResult>> action,
        CancellationToken ct)
    {
        var attempts = new List<(string Provider, FailoverReason Reason, string Message)>();

        foreach (var (name, provider) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (cooldowns.IsInCooldown(name))
            {
                LogSkippingCooldown(name);
                attempts.Add((name, FailoverReason.Unknown, "In cooldown"));
                continue;
            }

            try
            {
                var result = await action(name, provider, ct);
                cooldowns.RecordSuccess(name);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ErrorClassifier.Classify(ex);

                if (!ErrorClassifier.IsRetriable(reason))
                {
                    LogNonRetriableError(name, reason.ToString());
                    throw; // Format errors abort immediately
                }

                var retryAfter = ParseRetryAfter(ex.Message);
                cooldowns.RecordFailure(name, reason, retryAfter);

                LogProviderFailed(name, reason.ToString(), ex.Message);
                attempts.Add((name, reason, ex.Message));
            }
        }

        throw new FallbackExhaustedException(attempts);
    }

    /// <summary>
    ///     Execute a streaming provider call with fallback across candidates.
    ///     Tries each streaming candidate in order, skipping those in cooldown.
    ///     Once the first chunk is successfully received from a provider, commits to that stream.
    ///     Non-retriable errors (Format) abort immediately without fallback.
    /// </summary>
    /// <param name="candidates">Ordered list of streaming provider candidates.</param>
    /// <param name="request">Base chat request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="requestTransform">
    ///     Optional per-candidate request transform. Receives the candidate name and base request,
    ///     returns a (possibly modified) request. Used for per-fallback model overrides.
    /// </param>
    public async IAsyncEnumerable<StreamChunk> ExecuteStreamAsync(
        IReadOnlyList<(string Name, IStreamingProvider Provider)> candidates,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct,
        Func<string, ChatRequest, ChatRequest>? requestTransform = null)
    {
        var attempts = new List<(string Provider, FailoverReason Reason, string Message)>();

        foreach (var (name, provider) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (cooldowns.IsInCooldown(name))
            {
                LogSkippingCooldown(name);
                attempts.Add((name, FailoverReason.Unknown, "In cooldown"));
                continue;
            }

            var effectiveRequest = request;
            if (requestTransform is not null)
            {
                effectiveRequest = requestTransform(name, request);
            }

            // Try to obtain the first chunk — if that succeeds, commit to this provider.
            // We buffer the first chunk so we can catch startup failures without yield-in-try issues.
            // The enumerator is wrapped in try/finally to guarantee disposal even on empty streams.
            StreamChunk? firstChunk;
            IAsyncEnumerator<StreamChunk>? enumerator = null;
            bool hasFirst;

            try
            {
                enumerator = provider.StreamAsync(effectiveRequest, ct).GetAsyncEnumerator(ct);
                hasFirst = await enumerator.MoveNextAsync();
                firstChunk = hasFirst ? enumerator.Current : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Dispose the enumerator on error — it may hold an HTTP connection.
                if (enumerator is not null)
                {
                    await enumerator.DisposeAsync();
                }

                var reason = ErrorClassifier.Classify(ex);

                if (!ErrorClassifier.IsRetriable(reason))
                {
                    LogNonRetriableError(name, reason.ToString());
                    throw;
                }

                var retryAfter = ParseRetryAfter(ex.Message);
                cooldowns.RecordFailure(name, reason, retryAfter);

                LogProviderFailed(name, reason.ToString(), ex.Message);
                attempts.Add((name, reason, ex.Message));
                continue;
            }

            // First chunk obtained — commit to this provider
            cooldowns.RecordSuccess(name);

            if (firstChunk is not null)
            {
                yield return firstChunk;
            }

            // Yield remaining chunks (errors here propagate to caller — no fallback mid-stream).
            // Disposal is handled manually so that exceptions from DisposeAsync are logged
            // rather than propagated (which would mask the actual stream result).
            try
            {
                while (hasFirst && await enumerator.MoveNextAsync())
                {
                    yield return enumerator.Current;
                }
            }
            finally
            {
                try
                {
                    await enumerator.DisposeAsync();
                }
                catch (Exception disposeEx)
                {
                    LogEnumeratorDisposeFailed(disposeEx);
                }
            }

            yield break;
        }

        throw new FallbackExhaustedException(attempts);
    }

    /// <summary>
    ///     Parse a retry-after duration from an error message.
    ///     Looks for patterns like "retry-after: 30" or "retry_after: 2.5".
    /// </summary>
    private static TimeSpan? ParseRetryAfter(string message)
    {
        var match = RetryAfterRegex().Match(message);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    [GeneratedRegex(@"retry[_-]after[:\s]+(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase, 200)]
    private static partial Regex RetryAfterRegex();

    [LoggerMessage(EventId = 100, Level = LogLevel.Warning,
        Message = "Skipping provider '{ProviderName}' — in cooldown")]
    private partial void LogSkippingCooldown(string providerName);

    [LoggerMessage(EventId = 101, Level = LogLevel.Error,
        Message = "Provider '{ProviderName}' returned non-retriable error ({Reason}), aborting fallback")]
    private partial void LogNonRetriableError(string providerName, string reason);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
        Message = "Provider '{ProviderName}' failed ({Reason}): {ErrorMessage}, trying next")]
    private partial void LogProviderFailed(string providerName, string reason, string errorMessage);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning,
        Message = "Stream enumerator DisposeAsync failed — connection may have leaked")]
    private partial void LogEnumeratorDisposeFailed(Exception exception);
}