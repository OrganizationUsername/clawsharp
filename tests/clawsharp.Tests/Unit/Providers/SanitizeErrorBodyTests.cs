using Clawsharp.Providers;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Unit tests for <see cref="ProviderRequestHandler.SanitizeErrorBody"/>.
/// Verifies that API keys, bearer tokens, and long hex strings are redacted,
/// and that oversized error bodies are truncated.
/// </summary>
[TestFixture]
public sealed class SanitizeErrorBodyTests
{
    // =========================================================================
    // 1. Clean Input — Pass-Through
    // =========================================================================

    [Test]
    public void CleanErrorBody_PassesThroughUnchanged()
    {
        const string input = """{"error":{"message":"Rate limit exceeded","type":"rate_limit_error"}}""";
        ProviderRequestHandler.SanitizeErrorBody(input).ShouldBe(input);
    }

    [Test]
    public void EmptyString_ReturnsEmpty()
    {
        ProviderRequestHandler.SanitizeErrorBody("").ShouldBe("");
    }

    // =========================================================================
    // 2. OpenAI-Style Keys (sk-...)
    // =========================================================================

    [Test]
    public void OpenAiKey_IsRedacted()
    {
        // sk- followed by 20+ alphanumeric chars (no dashes — the regex is sk-[A-Za-z0-9]{20,})
        const string input = """Incorrect API key provided: sk-projabc123def456ghi789jkl0. You can find your API key at...""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("sk-projabc123def456ghi789jkl0");
        result.ShouldContain("[REDACTED]");
        result.ShouldContain("Incorrect API key provided:");
    }

    [Test]
    public void OpenAiKeyShortPrefix_IsRedacted()
    {
        // Plain sk- prefix with 26 alphanumeric chars after it
        const string input = """Invalid key: sk-abcdefghijklmnopqrstuvwxyz""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("sk-abcdefghijklmnopqrstuvwxyz");
        result.ShouldContain("[REDACTED]");
    }

    // =========================================================================
    // 3. Anthropic-Style Keys (sk-ant-...)
    // =========================================================================

    [Test]
    public void AnthropicKey_IsRedacted()
    {
        const string input = """{"error":{"type":"authentication_error","message":"invalid x-api-key sk-ant-api03-abcdef1234567890abcdef1234567890"}}""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("sk-ant-api03-abcdef1234567890abcdef1234567890");
        result.ShouldContain("[REDACTED]");
        result.ShouldContain("authentication_error");
    }

    [Test]
    public void AnthropicKeyWithDashes_IsFullyRedacted()
    {
        // Anthropic keys contain dashes in the body; the regex allows [A-Za-z0-9\-]{20,}
        const string input = """key: sk-ant-api03-abc-def-ghi-jkl-mno-pqr-stu""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("sk-ant-api03");
        result.ShouldContain("[REDACTED]");
    }

    // =========================================================================
    // 4. key-... Style Keys
    // =========================================================================

    [Test]
    public void KeyPrefixedToken_IsRedacted()
    {
        const string input = """API error: invalid key-abcdefghijklmnopqrstuvwxyz1234""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("key-abcdefghijklmnopqrstuvwxyz1234");
        result.ShouldContain("[REDACTED]");
    }

    // =========================================================================
    // 5. Bearer Tokens
    // =========================================================================

    [Test]
    public void BearerToken_IsRedacted()
    {
        const string input = """Authorization header: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
        result.ShouldContain("[REDACTED]");
        result.ShouldContain("Authorization header:");
    }

    [Test]
    public void BearerTokenWithTabSeparator_IsRedacted()
    {
        // Bearer followed by whitespace (regex uses \s+)
        var input = "Bearer\teyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.some.long.token.value";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
        result.ShouldContain("[REDACTED]");
    }

    // =========================================================================
    // 6. Long Hex Strings (40+ Characters)
    // =========================================================================

    [Test]
    public void LongHexString_IsRedacted()
    {
        // 40-char hex string (e.g. SHA-1 hash or API key)
        const string hex = "a1b2c3d4e5f6a7b8c9d0a1b2c3d4e5f6a7b8c9d0";
        var input = $"""Error processing request for token {hex}""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain(hex);
        result.ShouldContain("[REDACTED]");
    }

    [Test]
    public void HexStringUnder40Chars_IsNotRedacted()
    {
        // 32-char hex string should NOT be redacted (below 40-char threshold)
        const string hex = "a1b2c3d4e5f6a7b8c9d0a1b2c3d4e5f6";
        var input = $"Request ID: {hex}";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldContain(hex);
    }

    [Test]
    public void UppercaseHexString_IsRedacted()
    {
        const string hex = "A1B2C3D4E5F6A7B8C9D0A1B2C3D4E5F6A7B8C9D0";
        var input = $"Hash: {hex}";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain(hex);
        result.ShouldContain("[REDACTED]");
    }

    // =========================================================================
    // 7. Multiple Secrets in Same Body
    // =========================================================================

    [Test]
    public void MultipleSecrets_AllRedacted()
    {
        const string input =
            """Key sk-projabc123def456ghi789jkl0 failed. """ +
            """Tried Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature next. """ +
            """Also found a1b2c3d4e5f6a7b8c9d0a1b2c3d4e5f6a7b8c9d0 in log.""";

        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("sk-projabc123def456ghi789jkl0");
        result.ShouldNotContain("eyJhbGciOiJIUzI1NiJ9");
        result.ShouldNotContain("a1b2c3d4e5f6a7b8c9d0a1b2c3d4e5f6a7b8c9d0");

        // Three distinct secrets should produce three [REDACTED] markers
        var redactedCount = result.Split("[REDACTED]").Length - 1;
        redactedCount.ShouldBe(3);
    }

    // =========================================================================
    // 8. Truncation (MaxSanitizedErrorLength = 500)
    // =========================================================================

    [Test]
    public void BodyOverMaxLength_IsTruncated()
    {
        var input = new string('x', 600);
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldEndWith("... (truncated)");
        // 500 chars + "... (truncated)" suffix
        result.Length.ShouldBe(500 + "... (truncated)".Length);
    }

    [Test]
    public void BodyExactlyAtMaxLength_IsNotTruncated()
    {
        var input = new string('y', 500);
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldBe(input);
        result.ShouldNotContain("truncated");
    }

    [Test]
    public void BodyOneOverMaxLength_IsTruncated()
    {
        var input = new string('z', 501);
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldEndWith("... (truncated)");
    }

    [Test]
    public void TruncationHappensAfterRedaction()
    {
        // A long body with a secret near the start — secret should be redacted
        // even though the body will also be truncated.
        // Use 'x' padding (not hex-valid) so the long tail isn't matched by [0-9a-fA-F]{40,}.
        var input = "Error: sk-projabc123def456ghi789jkl0 " + new string('x', 600);
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain("sk-projabc123def456ghi789jkl0");
        result.ShouldContain("[REDACTED]");
        result.ShouldEndWith("... (truncated)");
    }

    // =========================================================================
    // 9. Edge Cases
    // =========================================================================

    [Test]
    public void ShortSkPrefix_NotRedacted()
    {
        // "sk-" followed by fewer than 20 chars should not match
        const string input = """Error with key sk-short""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldContain("sk-short");
    }

    [Test]
    public void BearerWithShortToken_NotRedacted()
    {
        // "Bearer " followed by fewer than 20 chars should not match
        const string input = "Bearer abc123";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldBe("Bearer abc123");
    }

    [Test]
    public void JsonErrorWithNoSecrets_PassesThrough()
    {
        const string input = """{"error":{"message":"The model `gpt-5` does not exist","type":"invalid_request_error","code":"model_not_found"}}""";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldBe(input);
    }

    [Test]
    public void SecretAtStartOfBody_IsRedacted()
    {
        const string input = "sk-projabc123def456ghi789jkl0 is invalid";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldStartWith("[REDACTED]");
        result.ShouldContain("is invalid");
    }

    [Test]
    public void SecretAtEndOfBody_IsRedacted()
    {
        const string input = "Invalid API key: sk-projabc123def456ghi789jkl0";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldEndWith("[REDACTED]");
        result.ShouldContain("Invalid API key:");
    }

    [TestCase("sk-ant-api03-longkeyvalue12345678")]
    [TestCase("sk-projABCDEFGHIJKLMNOPQRST")]
    [TestCase("key-abcdefghijklmnopqrstuvwxyz")]
    public void VariousKeyFormats_AreRedacted(string key)
    {
        var input = $"Auth failed: {key}";
        var result = ProviderRequestHandler.SanitizeErrorBody(input);

        result.ShouldNotContain(key);
        result.ShouldContain("[REDACTED]");
    }
}
