using System.Net;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Tests.Unit.Core;

/// <summary>
/// Unit tests for <see cref="ErrorClassifier"/>.
/// Verifies HTTP status code classification, message-based pattern matching,
/// exception chain walking, <see cref="ErrorClassifier.IsRetriable"/>, and edge cases.
/// </summary>
[TestFixture]
public sealed class ErrorClassifierTests
{
    // =========================================================================
    // 1. HTTP Status Code Classification
    // =========================================================================

    [TestCase(HttpStatusCode.TooManyRequests, FailoverReason.RateLimit)] // 429
    [TestCase(HttpStatusCode.Unauthorized, FailoverReason.Auth)] // 401
    [TestCase(HttpStatusCode.Forbidden, FailoverReason.Auth)] // 403
    [TestCase(HttpStatusCode.PaymentRequired, FailoverReason.Billing)] // 402
    [TestCase(HttpStatusCode.RequestTimeout, FailoverReason.Timeout)] // 408
    [TestCase(HttpStatusCode.BadRequest, FailoverReason.Format)] // 400
    [TestCase(HttpStatusCode.InternalServerError, FailoverReason.Timeout)] // 500
    [TestCase(HttpStatusCode.ServiceUnavailable, FailoverReason.Timeout)] // 503
    [TestCase(HttpStatusCode.BadGateway, FailoverReason.Timeout)] // 502
    [TestCase(HttpStatusCode.GatewayTimeout, FailoverReason.Timeout)] // 504
    public void Classify_HttpStatusCode_ReturnsExpectedReason(
        HttpStatusCode statusCode, FailoverReason expected)
    {
        var ex = new HttpRequestException("server error", inner: null, statusCode);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(expected);
    }

    [Test]
    public void Classify_HttpRequestException_NullStatusCode_FallsToMessageMatching()
    {
        // No status code, but message contains a rate-limit pattern
        var ex = new HttpRequestException("too many requests");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.RateLimit);
    }

    [Test]
    public void Classify_HttpRequestException_NullStatusCode_NoMatchingMessage_ReturnsUnknown()
    {
        var ex = new HttpRequestException("something unexpected happened");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Unknown);
    }

    // =========================================================================
    // 2. Message-Based Classification: Rate Limit
    // =========================================================================

    [TestCase("too many requests")]
    [TestCase("Too Many Requests")]
    [TestCase("Error 429: limit reached")]
    [TestCase("resource_exhausted")]
    [TestCase("quota exceeded")]
    [TestCase("usage limit reached")]
    [TestCase("You have exceeded your current quota")]
    [TestCase("rate_limit hit")]
    [TestCase("rate limit exceeded")]
    [TestCase("The resource has been exhausted")]
    [TestCase("resource was exhausted by concurrent calls")]
    public void Classify_RateLimitMessage_ReturnsRateLimit(string message)
    {
        var ex = new InvalidOperationException(message);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.RateLimit);
    }

    // =========================================================================
    // 3. Message-Based Classification: Billing
    // =========================================================================

    [TestCase("payment required")]
    [TestCase("insufficient credits")]
    [TestCase("credit balance too low")]
    [TestCase("Visit plans & billing to upgrade")]
    [TestCase("insufficient balance")]
    [TestCase("Error 402: payment needed")]
    public void Classify_BillingMessage_ReturnsBilling(string message)
    {
        var ex = new InvalidOperationException(message);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Billing);
    }

    // =========================================================================
    // 4. Message-Based Classification: Timeout
    // =========================================================================

    [TestCase("connection timeout")]
    [TestCase("request timed out")]
    [TestCase("deadline exceeded")]
    [TestCase("context deadline exceeded")]
    [TestCase("TIMEOUT on upstream")]
    public void Classify_TimeoutMessage_ReturnsTimeout(string message)
    {
        var ex = new InvalidOperationException(message);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Timeout);
    }

    // =========================================================================
    // 5. Message-Based Classification: Auth
    // =========================================================================

    [TestCase("incorrect api key provided")]
    [TestCase("invalid token")]
    [TestCase("authentication required")]
    [TestCase("please re-authenticate")]
    [TestCase("oauth token refresh failed")]
    [TestCase("unauthorized")]
    [TestCase("forbidden")]
    [TestCase("access denied")]
    [TestCase("token has expired")]
    [TestCase("session expired")]
    [TestCase("no credentials found")]
    [TestCase("no api key found")]
    [TestCase("invalid_api_key")]
    [TestCase("invalid api key")]
    [TestCase("invalid_apikey")]
    [TestCase("Response status code: 401")]
    [TestCase("HTTP 403 error")]
    public void Classify_AuthMessage_ReturnsAuth(string message)
    {
        var ex = new InvalidOperationException(message);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Auth);
    }

    // =========================================================================
    // 6. Message-Based Classification: Format (non-retriable)
    // =========================================================================

    [TestCase("string should match pattern")]
    [TestCase("tool_use.id is required")]
    [TestCase("tool_use_id invalid")]
    [TestCase("messages.1.content.1.tool_use.id must match")]
    [TestCase("invalid request format")]
    public void Classify_FormatMessage_ReturnsFormat(string message)
    {
        var ex = new InvalidOperationException(message);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Format);
    }

    // =========================================================================
    // 7. Message-Based Classification: Overloaded
    // =========================================================================

    [TestCase("server overloaded")]
    [TestCase("model is currently overloaded")]
    [TestCase("overloaded_error")]
    [TestCase("Overloaded_Error from Anthropic")]
    public void Classify_OverloadedMessage_ReturnsOverloaded(string message)
    {
        var ex = new InvalidOperationException(message);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Overloaded);
    }

    // =========================================================================
    // 8. Exception Chain Walking (inner exceptions)
    // =========================================================================

    [Test]
    public void Classify_InnerException_WithMatchingMessage_IsDetected()
    {
        var inner = new InvalidOperationException("quota exceeded");
        var outer = new Exception("Request failed", inner);

        var result = ErrorClassifier.Classify(outer);

        result.ShouldBe(FailoverReason.RateLimit);
    }

    [Test]
    public void Classify_InnerHttpRequestException_WithStatusCode_TakesPriority()
    {
        var inner = new HttpRequestException("error", null, HttpStatusCode.TooManyRequests);
        var outer = new Exception("Provider call failed", inner);

        var result = ErrorClassifier.Classify(outer);

        result.ShouldBe(FailoverReason.RateLimit);
    }

    [Test]
    public void Classify_DeeplyNestedInnerException_MatchesMessage()
    {
        var innermost = new InvalidOperationException("overloaded");
        var middle = new Exception("wrapper", innermost);
        var outer = new Exception("outer wrapper", middle);

        var result = ErrorClassifier.Classify(outer);

        result.ShouldBe(FailoverReason.Overloaded);
    }

    [Test]
    public void Classify_StatusCodeOnOuterException_WinsOverInnerMessage()
    {
        // The outer exception has a definitive status code; inner exception has
        // a message that would match a different category. Status code wins because
        // ErrorClassifier checks status codes first during the chain walk.
        var inner = new InvalidOperationException("overloaded");
        var outer = new HttpRequestException("error", inner, HttpStatusCode.Unauthorized);

        var result = ErrorClassifier.Classify(outer);

        result.ShouldBe(FailoverReason.Auth);
    }

    // =========================================================================
    // 9. IsRetriable
    // =========================================================================

    [TestCase(FailoverReason.RateLimit, true)]
    [TestCase(FailoverReason.Billing, true)]
    [TestCase(FailoverReason.Timeout, true)]
    [TestCase(FailoverReason.Auth, true)]
    [TestCase(FailoverReason.Overloaded, true)]
    [TestCase(FailoverReason.Unknown, true)]
    [TestCase(FailoverReason.Format, false)]
    public void IsRetriable_ReturnsExpected(FailoverReason reason, bool expected)
    {
        ErrorClassifier.IsRetriable(reason).ShouldBe(expected);
    }

    // =========================================================================
    // 10. Edge Cases
    // =========================================================================

    [Test]
    public void Classify_EmptyMessage_ReturnsUnknown()
    {
        var ex = new Exception(string.Empty);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Unknown);
    }

    [Test]
    public void Classify_GenericException_NoMatchingPattern_ReturnsUnknown()
    {
        var ex = new Exception("everything is fine");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Unknown);
    }

    [Test]
    public void Classify_CaseInsensitive_MatchesUpperCase()
    {
        var ex = new Exception("RESOURCE_EXHAUSTED");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.RateLimit);
    }

    [Test]
    public void Classify_CaseInsensitive_MatchesMixedCase()
    {
        var ex = new Exception("Payment Required");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Billing);
    }

    [Test]
    public void Classify_TaskCanceledException_MatchesTimeout()
    {
        // TaskCanceledException message typically contains "canceled" but not a
        // classifier pattern. With no status code and no matching keyword, it
        // returns Unknown -- unless the message happens to contain a known
        // pattern. This verifies the classifier does not special-case exception
        // types, only messages and status codes.
        var ex = new TaskCanceledException("A task was canceled.");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Unknown);
    }

    [Test]
    public void Classify_TaskCanceledException_WithTimeoutMessage_ReturnsTimeout()
    {
        var ex = new TaskCanceledException("The request timed out");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Timeout);
    }

    [Test]
    public void Classify_OperationCanceledException_NoPattern_ReturnsUnknown()
    {
        var ex = new OperationCanceledException("The operation was canceled.");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Unknown);
    }

    // =========================================================================
    // 11. Priority Order — first match wins
    // =========================================================================

    [Test]
    public void Classify_MessageMatchingMultipleCategories_RateLimitWinsOverAuth()
    {
        // "429" matches RateLimit; "unauthorized" matches Auth.
        // RateLimit patterns are checked first, so RateLimit should win.
        var ex = new Exception("Error 429: unauthorized request");

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.RateLimit);
    }

    [Test]
    public void Classify_HttpStatusCode_WinsOverMessagePattern()
    {
        // Status code 400 = Format, but message says "overloaded".
        // Status code is checked before message patterns.
        var ex = new HttpRequestException("overloaded", null, HttpStatusCode.BadRequest);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.Format);
    }

    [Test]
    public void Classify_UnrecognizedStatusCode_FallsToMessageMatching()
    {
        // 418 I'm a Teapot -- not in the switch, so ClassifyStatusCode returns Unknown,
        // then ClassifyMessage runs and picks up "rate limit" from the message.
        var ex = new HttpRequestException("rate limit exceeded", null, (HttpStatusCode)418);

        var result = ErrorClassifier.Classify(ex);

        result.ShouldBe(FailoverReason.RateLimit);
    }
}