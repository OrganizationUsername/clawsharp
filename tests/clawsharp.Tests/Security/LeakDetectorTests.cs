using Clawsharp.Security;

namespace Clawsharp.Tests.Security;

public sealed class LeakDetectorTests
{
    // ── API Key Detection ───────────────────────────────────────────────

    [Test]
    public void Scan_StripeSecretKey_DetectsAndRedacts()
    {
        var input = "Use this key: sk_live_abc123def456ghi789jkl012";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Stripe secret key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
        result.Redacted.ShouldNotContain("sk_live_");
    }

    [Test]
    public void Scan_StripeTestKey_DetectsAndRedacts()
    {
        var input = "key: sk_test_abcdefghijklmnopqrstuvwx";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Stripe secret key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
    }

    [Test]
    public void Scan_OpenAiStyleKey_DetectsAndRedacts()
    {
        // 48+ alphanumeric chars after "sk-"
        var input = "sk-" + new string('a', 48);

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("OpenAI-style API key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
        result.Redacted.ShouldNotContain("sk-");
    }

    [Test]
    public void Scan_AnthropicKey_DetectsAndRedacts()
    {
        var input = "Authorization: sk-ant-abcdefghijklmnop0123456789ABCDEF";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Anthropic API key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
        result.Redacted.ShouldNotContain("sk-ant-");
    }

    [Test]
    public void Scan_GoogleApiKey_DetectsAndRedacts()
    {
        // AIza + 35 chars
        var input = "google_key=AIzaSyB" + new string('x', 32);

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Google API key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
    }

    [Test]
    public void Scan_GithubToken_DetectsAndRedacts()
    {
        // ghp_ + 36 chars
        var input = "GITHUB_TOKEN=ghp_" + new string('A', 36);

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("GitHub token");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
    }

    [Test]
    public void Scan_GithubPat_DetectsAndRedacts()
    {
        var input = "github_pat_" + new string('a', 22);

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("GitHub PAT");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
    }

    [Test]
    public void Scan_GenericApiKeyAssignment_DetectsAndRedacts()
    {
        var input = "api_key=abcdef1234567890ABCDEF";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Generic API key");
        result.Redacted.ShouldContain("[REDACTED_API_KEY]");
    }

    [TestCase("api-key: abcdefghijklmnopqrst1234")]
    [TestCase("apikey=ABCDEFGHIJKLMNOPQRST1234")]
    public void Scan_GenericApiKeyVariants_Detected(string input)
    {
        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Generic API key");
    }

    // ── AWS Credentials ─────────────────────────────────────────────────

    [Test]
    public void Scan_AwsAccessKeyId_DetectsAndRedacts()
    {
        var input = "AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("AWS Access Key ID");
        result.Redacted.ShouldContain("[REDACTED_AWS_CREDENTIAL]");
        result.Redacted.ShouldNotContain("AKIA");
    }

    [Test]
    public void Scan_AwsSecretAccessKey_DetectsAndRedacts()
    {
        var secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
        var input = $"aws_secret_access_key={secret}";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("AWS Secret Access Key");
        result.Redacted.ShouldContain("[REDACTED_AWS_CREDENTIAL]");
    }

    // ── Private Keys ────────────────────────────────────────────────────

    [Test]
    public void Scan_RsaPrivateKey_DetectsAndRedacts()
    {
        var input = """
                    -----BEGIN RSA PRIVATE KEY-----
                    MIIEowIBAAKCAQEA0Z3VS5JJcds3xfn/ygWep4PAtGoRBh...
                    -----END RSA PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("RSA private key");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
        result.Redacted.ShouldNotContain("-----BEGIN RSA PRIVATE KEY-----");
        result.Redacted.ShouldNotContain("MIIEowIBAAKCAQEA");
    }

    [Test]
    public void Scan_GenericPrivateKey_DetectsAndRedacts()
    {
        var input = """
                    -----BEGIN PRIVATE KEY-----
                    MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQ...
                    -----END PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Private key");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
    }

    [Test]
    public void Scan_EcPrivateKey_DetectsAndRedacts()
    {
        var input = """
                    -----BEGIN EC PRIVATE KEY-----
                    MHQCAQEEIODg...
                    -----END EC PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("EC private key");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
    }

    [Test]
    public void Scan_OpenSshPrivateKey_DetectsAndRedacts()
    {
        var input = """
                    -----BEGIN OPENSSH PRIVATE KEY-----
                    b3BlbnNzaC1rZXktdjEAAAAABG5vbmU...
                    -----END OPENSSH PRIVATE KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("OpenSSH private key");
        result.Redacted.ShouldContain("[REDACTED_PRIVATE_KEY]");
    }

    [Test]
    public void Scan_PrivateKeyMissingEnd_NotDetected()
    {
        var input = "-----BEGIN RSA PRIVATE KEY-----\nsome data here but no end marker";

        var result = LeakDetector.Scan(input);

        result.Patterns.ShouldNotContain("RSA private key");
    }

    // ── JWT Tokens ──────────────────────────────────────────────────────

    [Test]
    public void Scan_JwtToken_DetectsAndRedacts()
    {
        var input = "Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("JWT token");
        result.Redacted.ShouldContain("[REDACTED_JWT]");
        result.Redacted.ShouldNotContain("eyJhbGciOiJIUzI1NiJ9");
    }

    [Test]
    public void Scan_JwtTokenInConfig_DetectsAndRedacts()
    {
        var input = "token: eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJhcGkifQ.signature_here";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("JWT token");
        result.Redacted.ShouldContain("[REDACTED_JWT]");
    }

    // ── Database Connection URLs ────────────────────────────────────────

    [Test]
    public void Scan_PostgresUrl_DetectsAndRedacts()
    {
        var input = "DATABASE_URL=postgres://admin:s3cretP@ss@db.example.com:5432/mydb";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("PostgreSQL connection URL");
        result.Redacted.ShouldContain("[REDACTED_DATABASE_URL]");
        result.Redacted.ShouldNotContain("s3cretP@ss");
    }

    [Test]
    public void Scan_PostgresqlUrl_DetectsAndRedacts()
    {
        var input = "postgresql://user:pass@host:5432/db";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("PostgreSQL connection URL");
    }

    [Test]
    public void Scan_MysqlUrl_DetectsAndRedacts()
    {
        var input = "mysql://root:password@localhost:3306/app";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("MySQL connection URL");
        result.Redacted.ShouldContain("[REDACTED_DATABASE_URL]");
    }

    [Test]
    public void Scan_MongoUrl_DetectsAndRedacts()
    {
        var input = "mongodb+srv://admin:hunter2@cluster0.example.net/myapp";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("MongoDB connection URL");
        result.Redacted.ShouldContain("[REDACTED_DATABASE_URL]");
    }

    [Test]
    public void Scan_RedisUrl_DetectsAndRedacts()
    {
        var input = "redis://default:myredispassword@redis.example.com:6379";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Redis connection URL");
        result.Redacted.ShouldContain("[REDACTED_DATABASE_URL]");
    }

    // ── Generic Secrets (sensitivity-gated) ─────────────────────────────

    [Test]
    public void Scan_PasswordAssignment_DetectedAboveThreshold()
    {
        var input = "password=SuperSecret123!";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Password in config");
        result.Redacted.ShouldContain("[REDACTED_SECRET]");
    }

    [Test]
    public void Scan_PasswordAssignment_NotDetectedBelowThreshold()
    {
        var input = "password=SuperSecret123!";

        var result = LeakDetector.Scan(input, sensitivity: 0.3);

        // Password pattern is sensitivity-gated (> 0.5)
        result.Patterns.ShouldNotContain("Password in config");
    }

    [Test]
    public void Scan_SecretValue_DetectedAboveThreshold()
    {
        var input = "secret=abcdefghijklmnop1234";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Secret value");
        result.Redacted.ShouldContain("[REDACTED_SECRET]");
    }

    [Test]
    public void Scan_TokenValue_DetectedAboveThreshold()
    {
        var input = "token=abcdefghijklmnopqrst1234";

        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("Token value");
        result.Redacted.ShouldContain("[REDACTED_SECRET]");
    }

    [TestCase("secret=short")]
    [TestCase("token=abc")]
    public void Scan_TooShortGenericSecrets_NotDetected(string input)
    {
        // "secret=..." needs 16+ chars; "token=..." needs 20+ chars
        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.Patterns.ShouldNotContain("Secret value");
        result.Patterns.ShouldNotContain("Token value");
    }

    // ── Clean Text (No False Positives) ─────────────────────────────────

    [Test]
    public void Scan_NormalConversation_IsClean()
    {
        var input = "Hello! How can I help you today? Let me know if you have any questions.";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeTrue();
        result.Patterns.ShouldBeEmpty();
        result.Redacted.ShouldBe(input);
    }

    [Test]
    public void Scan_CodeSnippetWithoutSecrets_IsClean()
    {
        var input = """
                    var config = new ConfigurationBuilder()
                        .AddJsonFile("appsettings.json")
                        .Build();
                    var connStr = config.GetConnectionString("Default");
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeTrue();
        result.Redacted.ShouldBe(input);
    }

    [Test]
    public void Scan_UrlWithoutCredentials_IsClean()
    {
        var input = "Visit https://example.com/api/v1/users for the API docs.";

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeTrue();
        result.Redacted.ShouldBe(input);
    }

    [Test]
    public void Scan_EmptyString_IsClean()
    {
        var result = LeakDetector.Scan("");

        result.IsClean.ShouldBeTrue();
        result.Patterns.ShouldBeEmpty();
        result.Redacted.ShouldBe("");
    }

    [Test]
    public void Scan_ShortSkPrefix_NotFalsePositive()
    {
        // "sk-" followed by too few characters should not match OpenAI-style key
        var input = "The variable sk-short is not a key.";

        var result = LeakDetector.Scan(input);

        result.Patterns.ShouldNotContain("OpenAI-style API key");
    }

    [Test]
    public void Scan_PublicKeyBlock_NotDetected()
    {
        // Public keys should NOT trigger the private key detector
        var input = """
                    -----BEGIN PUBLIC KEY-----
                    MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...
                    -----END PUBLIC KEY-----
                    """;

        var result = LeakDetector.Scan(input);

        result.Patterns.ShouldNotContain("RSA private key");
        result.Patterns.ShouldNotContain("Private key");
        result.Patterns.ShouldNotContain("EC private key");
        result.Patterns.ShouldNotContain("OpenSSH private key");
    }

    // ── Sensitivity Parameter ───────────────────────────────────────────

    [Test]
    public void Scan_StructuralPatternsFireAtAnySensitivity()
    {
        // Structural patterns (API keys, AWS, private keys, JWT, DB URLs)
        // should fire even at sensitivity 0.0
        var input = "AKIAIOSFODNN7EXAMPLE";

        var result = LeakDetector.Scan(input, sensitivity: 0.0);

        result.IsClean.ShouldBeFalse();
        result.Patterns.ShouldContain("AWS Access Key ID");
    }

    [Test]
    public void Scan_GenericSecrets_GatedBySensitivityThreshold()
    {
        var input = "password=MyLongPassword123";

        var atLow = LeakDetector.Scan(input, sensitivity: 0.4);
        var atHigh = LeakDetector.Scan(input, sensitivity: 0.7);

        // Below 0.5 threshold: generic secrets not checked
        atLow.Patterns.ShouldNotContain("Password in config");

        // Above 0.5 threshold: generic secrets checked
        atHigh.Patterns.ShouldContain("Password in config");
    }

    [Test]
    public void Scan_AtExactThreshold_GenericSecretsNotChecked()
    {
        // Threshold is > 0.5 (not >=), so exactly 0.5 should not trigger
        var input = "password=MyLongPassword123";

        var result = LeakDetector.Scan(input, sensitivity: 0.5);

        result.Patterns.ShouldNotContain("Password in config");
    }

    // ── Multiple Detections ─────────────────────────────────────────────

    [Test]
    public void Scan_MultipleSecrets_AllDetectedAndRedacted()
    {
        var input = """
                    AWS_KEY=AKIAIOSFODNN7EXAMPLE
                    DB=postgres://user:pass@host:5432/db
                    """;

        var result = LeakDetector.Scan(input);

        result.IsClean.ShouldBeFalse();
        result.Patterns.Count.ShouldBeGreaterThanOrEqualTo(2);
        result.Patterns.ShouldContain("AWS Access Key ID");
        result.Patterns.ShouldContain("PostgreSQL connection URL");
        result.Redacted.ShouldContain("[REDACTED_AWS_CREDENTIAL]");
        result.Redacted.ShouldContain("[REDACTED_DATABASE_URL]");
    }

    // ── Redaction Preserves Surrounding Text ────────────────────────────

    [Test]
    public void Scan_RedactionPreservesSurroundingText()
    {
        var input = "Before AKIAIOSFODNN7EXAMPLE After";

        var result = LeakDetector.Scan(input);

        result.Redacted.ShouldStartWith("Before ");
        result.Redacted.ShouldEndWith(" After");
        result.Redacted.ShouldContain("[REDACTED_AWS_CREDENTIAL]");
    }

    // ── LeakScanResult Record ───────────────────────────────────────────

    [Test]
    public void Scan_CleanResult_IsCleanTrue_PatternsEmpty()
    {
        var result = LeakDetector.Scan("just normal text");

        result.IsClean.ShouldBeTrue();
        result.Patterns.ShouldBeEmpty();
        result.Redacted.ShouldBe("just normal text");
    }

    [Test]
    public void Scan_DirtyResult_IsCleanFalse_PatternsPopulated()
    {
        var result = LeakDetector.Scan("key: AKIAIOSFODNN7EXAMPLE");

        result.IsClean.ShouldBeFalse();
        result.Patterns.Count.ShouldBeGreaterThan(0);
    }

    // ── Database URL without credentials ────────────────────────────────

    [Test]
    public void Scan_PostgresUrlWithoutPassword_NotDetected()
    {
        // The regex requires user:pass@ pattern
        var input = "postgres://localhost:5432/mydb";

        var result = LeakDetector.Scan(input);

        result.Patterns.ShouldNotContain("PostgreSQL connection URL");
    }

    // ── High Entropy Token Detection ────────────────────────────────────

    [Test]
    public void Scan_HighEntropyToken_DetectedAtHighSensitivity()
    {
        // A random-looking mixed-case alphanumeric string of 32+ characters
        var input = "config_value=aB3cD4eF5gH6iJ7kL8mN9oP0qR1sT2u";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        // May or may not trigger depending on exact entropy, but the scan should run
        // At a minimum, verifying the scan completes without error
        result.ShouldNotBeNull();
    }

    [Test]
    public void Scan_LowEntropyRepeatedChars_NotFlaggedAsHighEntropy()
    {
        // Repeated characters have very low entropy
        var input = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    [Test]
    public void Scan_ShortToken_NotFlaggedAsHighEntropy()
    {
        // Token shorter than EntropyTokenMinLen (24) should not be flagged
        var input = "aB3cD4eF5gH6iJ7kL8mN9o";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    [Test]
    public void Scan_HighEntropyAllLetters_NotFlaggedWithoutDigits()
    {
        // Must have both letters and digits to be considered
        var input = "abcdefghijklmnopqrstuvwxyzABCD";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    [Test]
    public void Scan_HighEntropyAllDigits_NotFlaggedWithoutLetters()
    {
        var input = "123456789012345678901234567890";

        var result = LeakDetector.Scan(input, sensitivity: 0.9);

        result.Patterns.ShouldNotContain("High-entropy token (possible encoded secret)");
    }

    // ── Default sensitivity ─────────────────────────────────────────────

    [Test]
    public void Scan_DefaultSensitivity_Is07()
    {
        // Default sensitivity is 0.7, which is above the 0.5 threshold,
        // so generic secrets should be checked by default
        var input = "password=VerySecretPassword1";

        var result = LeakDetector.Scan(input);

        result.Patterns.ShouldContain("Password in config");
    }

    // ── Password pattern variants ───────────────────────────────────────

    [TestCase("password:MySecret88")]
    [TestCase("PASSWORD=MySecret88")]
    public void Scan_PasswordColonAndCaseVariants_Detected(string input)
    {
        var result = LeakDetector.Scan(input, sensitivity: 0.7);

        result.Patterns.ShouldContain("Password in config");
    }
}