using System.Security.Cryptography;
using Clawsharp.Config;
using Clawsharp.Config.Security;
using Clawsharp.Security;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Security;

/// <summary>
/// Edge-case tests for <see cref="SecretStore"/> not covered by the main test class:
/// cross-instance shared-key round-trip, wrong-key tamper, use-after-dispose,
/// and non-hex key file content.
/// </summary>
[TestFixture]
public sealed class SecretStoreEdgeCaseTests
{
    private readonly string _originalEnvKey =
        Environment.GetEnvironmentVariable("CLAWSHARP_SECRET_KEY") ?? "";

    [SetUp]
    public void ClearEnvKey() =>
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", null);

    [TearDown]
    public void RestoreEnvKey() =>
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY",
            string.IsNullOrEmpty(_originalEnvKey) ? null : _originalEnvKey);

    private static SecretStore MakeStoreWithKey(string hexKey)
    {
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", hexKey);
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        return new SecretStore(Options.Create(config));
    }

    private static string RandomHexKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToHexString(key).ToLowerInvariant();
    }

    // ── Cross-instance shared key ─────────────────────────────────────

    [Test]
    public void CrossInstance_SameKey_DecryptsCorrectly()
    {
        // Scenario: encrypt with store A, decrypt with store B (different instance, same key).
        // This is the process-restart / multi-pod scenario.
        var sharedKey = RandomHexKey();

        using var storeA = MakeStoreWithKey(sharedKey);
        var ciphertext = storeA.Encrypt("cross-instance-secret");

        // Clear and re-set so storeB loads the same key from the env var
        using var storeB = MakeStoreWithKey(sharedKey);
        var plaintext = storeB.Decrypt(ciphertext);

        plaintext.ShouldBe("cross-instance-secret");
    }

    [Test]
    public void CrossInstance_SameKey_MultiplePlaintexts_AllRoundTrip()
    {
        var sharedKey = RandomHexKey();
        var plaintexts = new[] { "api-key-abc", "token-xyz-999", "sk-ant-very-long-secret-value-here" };

        using var storeA = MakeStoreWithKey(sharedKey);
        var ciphertexts = plaintexts.Select(p => storeA.Encrypt(p)).ToArray();

        using var storeB = MakeStoreWithKey(sharedKey);
        for (var i = 0; i < plaintexts.Length; i++)
        {
            storeB.Decrypt(ciphertexts[i]).ShouldBe(plaintexts[i]);
        }
    }

    // ── Wrong-key decrypt ─────────────────────────────────────────────

    [Test]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        var keyA = RandomHexKey();
        var keyB = RandomHexKey();

        using var storeA = MakeStoreWithKey(keyA);
        var ciphertext = storeA.Encrypt("some secret");

        using var storeB = MakeStoreWithKey(keyB);
        Should.Throw<CryptographicException>(() => storeB.Decrypt(ciphertext));
    }

    // ── Use after dispose ─────────────────────────────────────────────

    [Test]
    public void Encrypt_AfterDispose_ZeroedKeyProducesGarbageOrThrows()
    {
        // After Dispose(), the key array is zeroed. Subsequent Encrypt calls use a zero key.
        // The Lazy<T> is already created (value is the zeroed array), so Encrypt "succeeds"
        // but produces a ciphertext encrypted under the zero key — NOT the original key.
        // This test documents this behavior: the result must not equal a legitimately
        // encrypted value from before disposal.
        var key = RandomHexKey();
        var store = MakeStoreWithKey(key);

        var before = store.Encrypt("value-before-dispose");
        store.Dispose();

        // Encryption after dispose uses zeroed key material — the result is unpredictable
        // but must not be decryptable by a correctly-keyed instance.
        var after = store.Encrypt("value-after-dispose");

        // A freshly-keyed store cannot decrypt the post-dispose ciphertext
        using var freshStore = MakeStoreWithKey(key);
        Should.Throw<CryptographicException>(() => freshStore.Decrypt(after),
            "post-dispose ciphertext encrypted with zeroed key must not decrypt with the real key");

        // But the pre-dispose ciphertext still decrypts correctly
        freshStore.Decrypt(before).ShouldBe("value-before-dispose");
    }

    // ── Non-hex key file content ──────────────────────────────────────

    [Test]
    public void LoadKey_NonHexEnvVar_ThrowsCryptographicException()
    {
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", "not-a-valid-hex-string!!!");
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        // The key is loaded lazily on first Encrypt/Decrypt call
        Should.Throw<CryptographicException>(() => store.Encrypt("trigger-load"));
    }

    [Test]
    public void LoadKey_WrongLengthHexEnvVar_ThrowsCryptographicException()
    {
        // Valid hex, but only 16 bytes (32 hex chars) instead of the required 32 bytes (64 hex chars)
        var shortKey = Convert.ToHexString(new byte[16]).ToLowerInvariant();
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", shortKey);
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        Should.Throw<CryptographicException>(() => store.Encrypt("trigger-load"));
    }
}
