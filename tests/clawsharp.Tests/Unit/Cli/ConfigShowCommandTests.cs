using Clawsharp.Cli.Config;

namespace Clawsharp.Tests.Unit.Cli;

/// <summary>Tests for <see cref="ConfigShowCommand.Redact"/> and <see cref="KnownSecretFields"/>.</summary>
public sealed class ConfigShowCommandTests
{
    // ── Redact ───────────────────────────────────────────────────────────────

    [Test]
    public void Redact_NonNullString_ReturnsRedacted()
    {
        var result = ConfigShowCommand.Redact("sk-1234567890abcdef");

        result.ShouldBe("[redacted]");
    }

    [Test]
    public void Redact_ShortString_ReturnsRedacted()
    {
        var result = ConfigShowCommand.Redact("ab");

        result.ShouldBe("[redacted]");
    }

    [Test]
    public void Redact_EmptyString_ReturnsRedacted()
    {
        // An empty string is still "not null", so it should be redacted.
        var result = ConfigShowCommand.Redact("");

        result.ShouldBe("[redacted]");
    }

    [Test]
    public void Redact_NullString_ReturnsNotSet()
    {
        var result = ConfigShowCommand.Redact(null);

        result.ShouldBe("(not set)");
    }

    [Test]
    public void Redact_EncryptedValue_ReturnsRedacted()
    {
        var result = ConfigShowCommand.Redact("enc2:AAAAsomeencrypteddata");

        result.ShouldBe("[redacted]");
    }

    // ── KnownSecretFields coverage ──────────────────────────────────────────

    [Test]
    public void SecretFields_ContainsApiKey()
    {
        KnownSecretFields.All.ShouldContain("apiKey");
    }

    [Test]
    public void SecretFields_ContainsToken()
    {
        KnownSecretFields.All.ShouldContain("token");
    }

    [Test]
    public void SecretFields_ContainsBotToken()
    {
        KnownSecretFields.All.ShouldContain("botToken");
    }

    [Test]
    public void SecretFields_ContainsAppToken()
    {
        KnownSecretFields.All.ShouldContain("appToken");
    }

    [Test]
    public void SecretFields_ContainsAccessToken()
    {
        KnownSecretFields.All.ShouldContain("accessToken");
    }

    [Test]
    public void SecretFields_ContainsPassword()
    {
        KnownSecretFields.All.ShouldContain("password");
    }

    [Test]
    public void SecretFields_ContainsSecret()
    {
        KnownSecretFields.All.ShouldContain("secret");
    }

    [Test]
    public void SecretFields_ContainsNickServPassword()
    {
        KnownSecretFields.All.ShouldContain("nickServPassword");
    }

    [Test]
    public void SecretFields_ContainsPairingToken()
    {
        KnownSecretFields.All.ShouldContain("pairingToken");
    }

    [Test]
    public void SecretFields_ContainsAwsSecretAccessKey()
    {
        KnownSecretFields.All.ShouldContain("awsSecretAccessKey");
    }

    [Test]
    public void SecretFields_ContainsAwsSecretKey()
    {
        KnownSecretFields.All.ShouldContain("awsSecretKey");
    }

    [Test]
    public void SecretFields_ContainsWebhookKey()
    {
        KnownSecretFields.All.ShouldContain("webhookKey");
    }

    [Test]
    public void SecretFields_ContainsTranscriptionApiKey()
    {
        KnownSecretFields.All.ShouldContain("transcriptionApiKey");
    }

    [Test]
    public void SecretFields_ExpectedCount()
    {
        // There are exactly 16 known secret fields; this test catches accidental
        // additions or removals without corresponding test updates.
        KnownSecretFields.All.Count.ShouldBe(16);
    }

    [Test]
    public void SecretFields_IsCaseInsensitive()
    {
        // KnownSecretFields uses StringComparer.OrdinalIgnoreCase.
        KnownSecretFields.All.ShouldContain("APIKEY");
        KnownSecretFields.All.ShouldContain("ApiKey");
        KnownSecretFields.All.ShouldContain("apikey");
    }

    [Test]
    public void SecretFields_DoesNotContainNonSecrets()
    {
        KnownSecretFields.All.ShouldNotContain("model");
        KnownSecretFields.All.ShouldNotContain("provider");
        KnownSecretFields.All.ShouldNotContain("baseUrl");
        KnownSecretFields.All.ShouldNotContain("temperature");
        KnownSecretFields.All.ShouldNotContain("workspace");
    }
}