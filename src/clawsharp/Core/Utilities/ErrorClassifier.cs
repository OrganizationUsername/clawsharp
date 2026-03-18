using System.Net;
using System.Text.RegularExpressions;

namespace Clawsharp.Core.Utilities;

/// <summary>
///     Classifies provider exceptions into <see cref="FailoverReason" /> categories
///     using 40+ patterns. First match wins (checked in priority order).
/// </summary>
public static partial class ErrorClassifier
{
    /// <summary>
    ///     Classify an exception into a <see cref="FailoverReason" />.
    ///     Checks HTTP status codes first, then scans the exception message against known patterns.
    /// </summary>
    public static FailoverReason Classify(Exception exception)
    {
        // Walk the full exception chain for HTTP status codes and message patterns.
        var messageBuilder = new System.Text.StringBuilder();
        var current = exception;
        while (current is not null)
        {
            if (current is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var reason = ClassifyStatusCode(httpEx.StatusCode.Value);
                if (reason != FailoverReason.Unknown)
                {
                    return reason;
                }
            }

            if (messageBuilder.Length > 0)
            {
                messageBuilder.Append(' ');
            }

            messageBuilder.Append(current.Message);
            current = current.InnerException;
        }

        return ClassifyMessage(messageBuilder.ToString());
    }

    /// <summary>Returns whether the given reason is retriable (all except Format).</summary>
    public static bool IsRetriable(FailoverReason reason) => reason != FailoverReason.Format;

    private static FailoverReason ClassifyStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => FailoverReason.Auth, // 401
        HttpStatusCode.Forbidden => FailoverReason.Auth, // 403
        HttpStatusCode.PaymentRequired => FailoverReason.Billing, // 402
        HttpStatusCode.RequestTimeout => FailoverReason.Timeout, // 408
        HttpStatusCode.TooManyRequests => FailoverReason.RateLimit, // 429
        HttpStatusCode.BadRequest => FailoverReason.Format, // 400
        // 5xx — transient server errors, treat as timeout for retry
        >= HttpStatusCode.InternalServerError and < (HttpStatusCode)600 => FailoverReason.Timeout,
        _ => FailoverReason.Unknown
    };

    private static FailoverReason ClassifyMessage(string message)
    {
        // ── Rate Limit (10 patterns) ────────────────────────────────────────
        if (message.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("resource_exhausted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("exceeded your current quota", StringComparison.OrdinalIgnoreCase)
            || RateLimitRegex().IsMatch(message)
            || ExceededQuotaRegex().IsMatch(message)
            || ResourceExhaustedRegex().IsMatch(message)
            || ResourceExhausted2Regex().IsMatch(message))
        {
            return FailoverReason.RateLimit;
        }

        // ── Billing (6 patterns) ────────────────────────────────────────────
        if (message.Contains("payment required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient credits", StringComparison.OrdinalIgnoreCase)
            || message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
            || message.Contains("plans & billing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase)
            || Status402Regex().IsMatch(message))
        {
            return FailoverReason.Billing;
        }

        // ── Timeout (4 patterns) ────────────────────────────────────────────
        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || message.Contains("deadline exceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context deadline exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return FailoverReason.Timeout;
        }

        // ── Auth (14 patterns) ──────────────────────────────────────────────
        if (message.Contains("incorrect api key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid token", StringComparison.OrdinalIgnoreCase)
            || message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || message.Contains("re-authenticate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("oauth token refresh failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("token has expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no credentials found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no api key found", StringComparison.OrdinalIgnoreCase)
            || InvalidApiKeyRegex().IsMatch(message)
            || Status401Regex().IsMatch(message)
            || Status403Regex().IsMatch(message))
        {
            return FailoverReason.Auth;
        }

        // ── Format (5 patterns, NON-RETRIABLE) ─────────────────────────────
        if (message.Contains("string should match pattern", StringComparison.OrdinalIgnoreCase)
            || message.Contains("tool_use.id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("tool_use_id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("messages.1.content.1.tool_use.id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid request format", StringComparison.OrdinalIgnoreCase))
        {
            return FailoverReason.Format;
        }

        // ── Overloaded (mapped to RateLimit) ────────────────────────────────
        if (message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
            || OverloadedErrorRegex().IsMatch(message))
        {
            return FailoverReason.Overloaded;
        }

        return FailoverReason.Unknown;
    }

    // ── Source-generated regex patterns ──────────────────────────────────────

    [GeneratedRegex(@"rate[_ ]limit", RegexOptions.IgnoreCase, 200)]
    private static partial Regex RateLimitRegex();

    [GeneratedRegex(@"exceeded.*quota", RegexOptions.IgnoreCase, 200)]
    private static partial Regex ExceededQuotaRegex();

    [GeneratedRegex(@"resource has been exhausted", RegexOptions.IgnoreCase, 200)]
    private static partial Regex ResourceExhaustedRegex();

    [GeneratedRegex(@"resource.*exhausted", RegexOptions.IgnoreCase, 200)]
    private static partial Regex ResourceExhausted2Regex();

    [GeneratedRegex(@"\b402\b", RegexOptions.None, 200)]
    private static partial Regex Status402Regex();

    [GeneratedRegex(@"invalid[_ ]?api[_ ]?key", RegexOptions.IgnoreCase, 200)]
    private static partial Regex InvalidApiKeyRegex();

    [GeneratedRegex(@"\b401\b", RegexOptions.None, 200)]
    private static partial Regex Status401Regex();

    [GeneratedRegex(@"\b403\b", RegexOptions.None, 200)]
    private static partial Regex Status403Regex();

    [GeneratedRegex(@"overloaded_error", RegexOptions.IgnoreCase, 200)]
    private static partial Regex OverloadedErrorRegex();
}