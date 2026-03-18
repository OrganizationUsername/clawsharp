using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

[TestFixture]
public sealed class LeakDetectorEdgeCaseTests
{
    // ── HIGH-5: sensitivity=0 still catches structural patterns ──────────
    //
    // The SanitizeReply handler was fixed from `> 0` to `>= 0` so that
    // LeakDetector.Scan is always called. These tests verify that Scan
    // itself detects structural patterns even at sensitivity 0.

    [Test]
    public void Scan_Sensitivity0_DetectsAnthropicKey()
    {
        var input = "here is sk-ant-api03-abcdefghijklmnop1234567890abcdefghijklmnop";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Anthropic API key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
        result.Redacted.ShouldNotContain("sk-ant-");
    }

    [Test]
    public void Scan_Sensitivity0_DetectsAwsAccessKey()
    {
        var input = "AWS key: AKIAIOSFODNN7EXAMPLE";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("AWS Access Key ID");
        result.Redacted.ShouldContain("[REDACTED_AWS_CREDENTIAL]");
    }

    [Test]
    public void Scan_Sensitivity0_DetectsPrivateKey()
    {
        var input = """
                    -----BEGIN RSA PRIVATE KEY-----
                    MIIEowIBAAKCAQEA0Z3VS5JJcds3xfn
                    -----END RSA PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("RSA private key");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
    }

    [Test]
    public void Scan_Sensitivity0_DetectsJwt()
    {
        var input = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("JWT token");
    }

    [Test]
    public void Scan_Sensitivity0_DetectsDatabaseUrl()
    {
        var input = "postgres://admin:hunter2@db.example.com:5432/mydb";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("PostgreSQL connection URL");
    }

    [Test]
    public void Scan_Sensitivity0_SkipsGenericSecrets()
    {
        // Generic secrets (password=, token=) are gated behind sensitivity > 0.5
        var input = "password=SuperSecret123!";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.Patterns.ShouldNotContain("Password in config");
    }

    [Test]
    public void Scan_Sensitivity0_SkipsHighEntropyTokens()
    {
        // High-entropy scanning is also gated behind sensitivity > 0.5
        var input = "aB3cD4eF5gH6iJ7kL8mN9oP0qR1sT2u";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    // ── HIGH-6: CheckPrivateKeys index consistency after prior redactions ─
    //
    // The code was fixed so that CheckPrivateKeys searches the `redacted`
    // string (not `original`) for positions, avoiding index mismatch
    // when prior API key redactions changed the string length.

    [Test]
    public void Scan_ApiKeyAndPrivateKey_BothRedactedCorrectly()
    {
        // An API key (which gets redacted first, changing string length)
        // followed by a private key block. Both must be independently
        // and correctly redacted without garbled output.
        var input = """
                    Authorization: sk-ant-api03-abcdefghijklmnop1234567890abcdefghijklmnop
                    -----BEGIN RSA PRIVATE KEY-----
                    MIIEowIBAAKCAQEA0Z3VS5JJcds3xfn
                    -----END RSA PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Anthropic API key");
        result.Patterns.ShouldContain("RSA private key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
        // Verify no remnants of either pattern
        result.Redacted.ShouldNotContain("sk-ant-");
        result.Redacted.ShouldNotContain("-----BEGIN RSA PRIVATE KEY-----");
        result.Redacted.ShouldNotContain("-----END RSA PRIVATE KEY-----");
        result.Redacted.ShouldNotContain("MIIEowIBAAKCAQEA");
    }

    [Test]
    public void Scan_AwsKeyAndPrivateKey_BothRedactedWithCorrectBoundaries()
    {
        // AWS access key (20 chars) gets redacted to [REDACTED_AWS_CREDENTIAL]
        // (24 chars), shifting all subsequent indices. Private key must still
        // be found and redacted correctly.
        var input = """
                    AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
                    -----BEGIN PRIVATE KEY-----
                    MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQ
                    -----END PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("AWS Access Key ID");
        result.Patterns.ShouldContain("Private key");
        result.Redacted.ShouldContain("[REDACTED_AWS_CREDENTIAL]");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
        result.Redacted.ShouldNotContain("AKIA");
        result.Redacted.ShouldNotContain("-----BEGIN PRIVATE KEY-----");
    }

    [Test]
    public void Scan_MultipleApiKeysBeforePrivateKey_AllRedactedCorrectly()
    {
        // Multiple prior redactions that significantly change string length
        var input = """
                    stripe: sk_live_abc123def456ghi789jkl012
                    github: ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
                    -----BEGIN EC PRIVATE KEY-----
                    MHQCAQEEIODgSomeKeyData
                    -----END EC PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Stripe secret key");
        result.Patterns.ShouldContain("GitHub token");
        result.Patterns.ShouldContain("EC private key");
        result.Redacted.ShouldNotContain("sk_live_");
        result.Redacted.ShouldNotContain("ghp_");
        result.Redacted.ShouldNotContain("-----BEGIN EC PRIVATE KEY-----");
    }

    // ── MED-11: ADO.NET connection string format ─────────────────────────
    //
    // ADO.NET uses `Password=value;` format (not URL-style).
    // The LeakDetector has a PasswordRegex that matches `password=...`
    // patterns, but only above sensitivity 0.5.

    [Test]
    public void Scan_AdoNetConnectionString_PasswordDetectedAtHighSensitivity()
    {
        // The PasswordRegex requires 8+ non-whitespace/non-quote chars after `password=`.
        var input = "Server=db;Database=mydb;Password=MyS3cretP@ssword;";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Password in config");
        result.Redacted.ShouldContain("[REDACTED_SECRET]");
    }

    [Test]
    public void Scan_AdoNetConnectionString_NotDetectedAtLowSensitivity()
    {
        var input = "Server=db;Database=mydb;Password=MyS3cretP@ssword;";

        var result = LeakDetector.Scan(input, sensitivity: 0.3);

        // Password pattern is sensitivity-gated, so low sensitivity skips it
        result.Patterns.ShouldNotContain("Password in config");
    }

    [Test]
    public void Scan_AdoNetConnectionString_ShortPassword_KnownLimitation_NotDetected()
    {
        // KNOWN LIMITATION: Passwords shorter than 8 characters are not detected
        // by the PasswordRegex (requires 8+ chars after `password=`).
        var input = "Server=db;Database=mydb;Password=s3cret;";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.Patterns.ShouldNotContain("Password in config");
    }

    [Test]
    public void Scan_AdoNetConnectionString_KnownLimitation_FullStringNotDetectedAsConnectionUrl()
    {
        // KNOWN LIMITATION: The ADO.NET `Server=db;Database=mydb;Password=...;`
        // format is NOT detected as a "database connection URL" since the detector
        // only matches URL-style connection strings (postgres://user:pass@host).
        // Only the Password= portion is caught (at high sensitivity, with 8+ char passwords).
        var input = "Server=db;Database=mydb;Password=MyS3cretP@ssword;";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.Patterns.ShouldNotContain("PostgreSQL connection URL");
        result.Patterns.ShouldNotContain("MySQL connection URL");
        // But Password= is caught
        result.Patterns.ShouldContain("Password in config");
    }

    // ── MED-12: Slack webhook URLs ───────────────────────────────────────

    [Test]
    public void Scan_SlackWebhookUrl_KnownLimitation_NotDetected()
    {
        // KNOWN LIMITATION: Slack webhook URLs contain an opaque token but do not
        // match any of the structural patterns (API key prefixes, database URLs,
        // private key blocks, JWT format). They would only be caught by high-entropy
        // scanning if the path segments have enough entropy, which is not guaranteed.
        var input = "https://hooks.slack.com/services/T01234567/B01234567/xyzABC123secret456token";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        // Document: Slack webhook URLs are not currently detected.
        // The URL lacks user:pass@ credentials, so the database URL patterns miss it.
        // The path segments are separate tokens split by '/', each too short for
        // high-entropy scanning (which requires 24+ chars).
        result.Patterns.ShouldNotContain("PostgreSQL connection URL");
        result.Patterns.ShouldNotContain("Stripe secret key");
    }

    // ── LOW-10: Base64-encoded secrets ───────────────────────────────────

    [Test]
    public void Scan_Base64EncodedApiKey_KnownLimitation_NotDetectedAsApiKey()
    {
        // KNOWN LIMITATION: Base64-encoding an API key destroys the structural
        // prefix pattern (e.g., "sk-ant-") so the structural regex cannot match.
        // The base64 output may have high enough entropy to trigger the entropy
        // scanner at high sensitivity, but this is incidental, not reliable.
        var originalKey = "sk-ant-api03-abcdefghijklmnop1234567890abcdef";
        var base64Key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(originalKey));
        var input = $"encoded_key: {base64Key}";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        // The original prefix "sk-ant-" is not present in base64 output
        result.Patterns.ShouldNotContain("Anthropic API key");
    }

    // ── LOW-11: Shannon entropy with non-ASCII ──────────────────────────

    [Test]
    public void Scan_CjkCharacters_DoesNotThrow()
    {
        // Non-ASCII characters are skipped by ShannonEntropy (only counts c < 128).
        // Verify that CJK content does not throw or produce false positives.
        var input = "这是一段中文文本用于测试安全扫描器的行为";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.ShouldNotBeNull();
        // CJK text should not be flagged as high-entropy since ShannonEntropy
        // skips non-ASCII chars (inRange == 0 → entropy 0)
        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    [Test]
    public void Scan_EmojiContent_DoesNotThrow()
    {
        var input = "Here are some emoji: \U0001F600\U0001F680\U0001F4A5\U0001F525\U0001F30D\U0001F3AE\U0001F4BB\U0001F916";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.ShouldNotBeNull();
    }

    [Test]
    public void Scan_MixedAsciiAndNonAscii_HighEntropy_OnlyAsciiConsidered()
    {
        // A token with mixed ASCII + non-ASCII: entropy is calculated only on ASCII chars.
        // The non-ASCII chars are skipped by ShannonEntropy, so a token like
        // "aB3中cD4文eF5" has its entropy computed from only the ASCII portion.
        // This token is too short to trigger (< 24 chars), but verify no crash.
        var input = "aB3中cD4文eF5国gH6际iJ7测kL8试";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.ShouldNotBeNull();
    }

    [Test]
    public void Scan_LongNonAsciiOnlyToken_NotFlaggedAsHighEntropy()
    {
        // A 30-char token made entirely of non-ASCII characters.
        // ShannonEntropy returns 0 since no chars are < 128.
        // ExtractCandidateTokens also requires IsAsciiLetterOrDigit, so this
        // entire string is likely not even extracted as a candidate token.
        var input = new string('\u4e00', 30); // 30x CJK character

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    // ── Additional edge cases ────────────────────────────────────────────

    [Test]
    public void Scan_OpenSshKeyAfterMultipleRedactions_CorrectBoundaries()
    {
        // OpenSSH private key after JWT + Stripe key: tests that three
        // sequential redactions with different replacement lengths work correctly.
        var input = """
                    token: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U
                    stripe: sk_live_abc123def456ghi789jkl012
                    -----BEGIN OPENSSH PRIVATE KEY-----
                    b3BlbnNzaC1rZXktdjEAAAAABG5vbmU
                    -----END OPENSSH PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("JWT token");
        result.Patterns.ShouldContain("Stripe secret key");
        result.Patterns.ShouldContain("OpenSSH private key");
        result.Redacted.ShouldContain("[REDACTED_JWT]");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
        result.Redacted.ShouldNotContain("eyJ");
        result.Redacted.ShouldNotContain("sk_live_");
        result.Redacted.ShouldNotContain("-----BEGIN OPENSSH PRIVATE KEY-----");
    }
}
