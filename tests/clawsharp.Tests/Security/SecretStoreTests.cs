using System.Security.Cryptography;
using Clawsharp.Config;
using Clawsharp.Config.Security;
using Clawsharp.Security;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Security;

public sealed class SecretStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keyPath;
    private string? _originalEnvKey;

    public SecretStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _keyPath = Path.Combine(_tempDir, ".secret_key");

        // Save and clear the env var so tests control key loading via file
        _originalEnvKey = Environment.GetEnvironmentVariable("CLAWSHARP_SECRET_KEY");
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", null);
    }

    public void Dispose()
    {
        // Restore original env var
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", _originalEnvKey);

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private SecretStore CreateStore(bool encrypt = true)
    {
        // Generate a deterministic key file for testing
        EnsureKeyFile();

        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = encrypt } };
        return new SecretStore(Options.Create(config));
    }

    private void EnsureKeyFile()
    {
        if (File.Exists(_keyPath))
        {
            return;
        }

        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        File.WriteAllText(_keyPath, Convert.ToHexString(key).ToLowerInvariant());
    }

    private SecretStore CreateStoreWithEnvKey(bool encrypt = true)
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", Convert.ToHexString(key).ToLowerInvariant());

        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = encrypt } };
        return new SecretStore(Options.Create(config));
    }

    // ── Encrypt/Decrypt round-trip ──────────────────────────────────────

    [TestCase("hello world")]
    [TestCase("sk-ant-api-key-1234567890")]
    [TestCase("a")]
    [TestCase("unicode: \u00e9\u00e8\u00ea\u00eb \u2603 \ud83d\ude00")]
    [TestCase("special chars: !@#$%^&*()_+-=[]{}|;':\",./<>?")]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal(string plaintext)
    {
        using var store = CreateStoreWithEnvKey();

        var encrypted = store.Encrypt(plaintext);
        var decrypted = store.Decrypt(encrypted);

        decrypted.ShouldBe(plaintext);
    }

    [Test]
    public void Encrypt_ProducesEnc2Prefix()
    {
        using var store = CreateStoreWithEnvKey();

        var encrypted = store.Encrypt("test value");

        encrypted.ShouldStartWith("enc2:");
    }

    [Test]
    public void Encrypt_DifferentNonceEachTime()
    {
        using var store = CreateStoreWithEnvKey();

        var enc1 = store.Encrypt("same value");
        var enc2 = store.Encrypt("same value");

        // Random nonce means ciphertext differs each time
        enc1.ShouldNotBe(enc2);

        // But both decrypt to the same value
        store.Decrypt(enc1).ShouldBe("same value");
        store.Decrypt(enc2).ShouldBe("same value");
    }

    // ── enc2: prefix detection (idempotent encrypt) ─────────────────────

    [Test]
    public void Encrypt_AlreadyEncrypted_ReturnedUnchanged()
    {
        using var store = CreateStoreWithEnvKey();

        var encrypted = store.Encrypt("my secret");
        var doubleEncrypted = store.Encrypt(encrypted);

        doubleEncrypted.ShouldBe(encrypted);
    }

    // ── Tamper detection ────────────────────────────────────────────────

    [Test]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        using var store = CreateStoreWithEnvKey();

        var encrypted = store.Encrypt("sensitive data");

        // Flip a byte in the hex payload (after "enc2:" prefix)
        var hex = encrypted["enc2:".Length..];
        var bytes = Convert.FromHexString(hex);

        // Tamper with the ciphertext portion (after the 12-byte nonce)
        bytes[14] ^= 0xFF;

        var tampered = "enc2:" + Convert.ToHexString(bytes).ToLowerInvariant();

        Should.Throw<CryptographicException>(() => store.Decrypt(tampered));
    }

    [Test]
    public void Decrypt_TruncatedCiphertext_ThrowsCryptographicException()
    {
        using var store = CreateStoreWithEnvKey();

        var encrypted = store.Encrypt("sensitive data");

        // Truncate to only the prefix + a few hex chars (less than nonce + tag)
        var truncated = encrypted[..15];

        Should.Throw<CryptographicException>(() => store.Decrypt(truncated));
    }

    [Test]
    public void Decrypt_InvalidHex_ThrowsCryptographicException()
    {
        using var store = CreateStoreWithEnvKey();

        Should.Throw<CryptographicException>(() => store.Decrypt("enc2:not-valid-hex!@#$"));
    }

    // ── Null/empty handling ─────────────────────────────────────────────

    [Test]
    public void Encrypt_Null_ReturnsEmpty()
    {
        using var store = CreateStoreWithEnvKey();

        var result = store.Encrypt(null);

        result.ShouldBe("");
    }

    [Test]
    public void Encrypt_Empty_ReturnsEmpty()
    {
        using var store = CreateStoreWithEnvKey();

        var result = store.Encrypt("");

        result.ShouldBe("");
    }

    [Test]
    public void Decrypt_Null_ReturnsEmpty()
    {
        using var store = CreateStoreWithEnvKey();

        var result = store.Decrypt(null);

        result.ShouldBe("");
    }

    [Test]
    public void Decrypt_Empty_ReturnsEmpty()
    {
        using var store = CreateStoreWithEnvKey();

        var result = store.Decrypt("");

        result.ShouldBe("");
    }

    [Test]
    public void Decrypt_Plaintext_ReturnedAsIs()
    {
        using var store = CreateStoreWithEnvKey();

        var result = store.Decrypt("just a plain string");

        result.ShouldBe("just a plain string");
    }

    // ── Disabled mode ───────────────────────────────────────────────────

    [Test]
    public void Encrypt_Disabled_ReturnsPlaintext()
    {
        using var store = CreateStoreWithEnvKey(encrypt: false);

        var result = store.Encrypt("my secret");

        result.ShouldBe("my secret");
        result.ShouldNotStartWith("enc2:");
    }

    [Test]
    public void Decrypt_Disabled_ReturnsPlaintext()
    {
        // When disabled, decrypt returns the value as-is (plaintext passthrough)
        using var store = CreateStoreWithEnvKey(encrypt: false);

        var result = store.Decrypt("my secret");

        result.ShouldBe("my secret");
    }

    [Test]
    public void Encrypt_DisabledNull_ReturnsEmpty()
    {
        using var store = CreateStoreWithEnvKey(encrypt: false);

        var result = store.Encrypt(null);

        result.ShouldBe("");
    }

    // ── Dispose zeroing ─────────────────────────────────────────────────

    [Test]
    public void Dispose_AfterUse_DoesNotThrow()
    {
        var store = CreateStoreWithEnvKey();

        // Force key loading
        store.Encrypt("trigger key load");

        // Dispose should not throw
        Should.NotThrow(() => store.Dispose());
    }

    [Test]
    public void Dispose_WithoutUse_DoesNotThrow()
    {
        var store = CreateStoreWithEnvKey();

        // Dispose without ever loading the key
        Should.NotThrow(() => store.Dispose());
    }

    [Test]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var store = CreateStoreWithEnvKey();
        store.Encrypt("trigger key load");

        Should.NotThrow(() =>
        {
            store.Dispose();
            store.Dispose();
        });
    }

    // ── Default config (null Secrets section) ───────────────────────────

    [Test]
    public void Encrypt_DefaultConfig_EncryptionEnabled()
    {
        // When Secrets is null, _enabled defaults to true
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());

        var config = new AppConfig(); // Secrets is null, default Encrypt=true
        using var store = new SecretStore(Options.Create(config));

        var result = store.Encrypt("test value");

        result.ShouldStartWith("enc2:");
    }

    // ── Environment variable key loading ────────────────────────────────

    [Test]
    public void Encrypt_WithEnvKey_Works()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", Convert.ToHexString(key).ToLowerInvariant());

        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        var encrypted = store.Encrypt("env key test");
        var decrypted = store.Decrypt(encrypted);

        decrypted.ShouldBe("env key test");
    }

    // ── Large plaintext ─────────────────────────────────────────────────

    [Test]
    public void EncryptDecrypt_LargePayload_RoundTrips()
    {
        using var store = CreateStoreWithEnvKey();

        var large = new string('X', 100_000);
        var encrypted = store.Encrypt(large);
        var decrypted = store.Decrypt(encrypted);

        decrypted.ShouldBe(large);
    }
}
