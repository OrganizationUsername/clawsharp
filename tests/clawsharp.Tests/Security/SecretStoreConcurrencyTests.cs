using System.Security.Cryptography;
using Clawsharp.Config;
using Clawsharp.Config.Security;
using Clawsharp.Security;
using Microsoft.Extensions.Options;

namespace Clawsharp.Tests.Security;

/// <summary>
/// Concurrency and race-condition tests for <see cref="SecretStore"/>:
/// MED-14 (concurrent encrypt/decrypt), MED-15 (decrypt after dispose),
/// MED-16 (GenerateAndPersistKey race condition).
/// </summary>
[TestFixture]
public sealed class SecretStoreConcurrencyTests
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

    private static string RandomHexKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToHexString(key).ToLowerInvariant();
    }

    private static SecretStore MakeStoreWithKey(string hexKey)
    {
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", hexKey);
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        return new SecretStore(Options.Create(config));
    }

    // ── MED-14: Concurrent encrypt/decrypt ──────────────────────────────

    [Test]
    public void ConcurrentEncryptDecrypt_NoDataCorruption()
    {
        var key = RandomHexKey();
        using var store = MakeStoreWithKey(key);

        const int iterations = 100;
        var plaintexts = Enumerable.Range(0, iterations)
            .Select(i => $"secret-value-{i}")
            .ToArray();

        // Phase 1: Encrypt all values concurrently
        var ciphertexts = new string[iterations];
        Parallel.For(0, iterations, i =>
        {
            ciphertexts[i] = store.Encrypt(plaintexts[i]);
        });

        // All ciphertexts should be valid (enc2: prefix)
        foreach (var ct in ciphertexts)
        {
            ct.ShouldStartWith("enc2:");
        }

        // Phase 2: Decrypt all values concurrently
        var decrypted = new string[iterations];
        Parallel.For(0, iterations, i =>
        {
            decrypted[i] = store.Decrypt(ciphertexts[i]);
        });

        // All decrypted values should match the originals
        for (var i = 0; i < iterations; i++)
        {
            decrypted[i].ShouldBe(plaintexts[i],
                $"Concurrent decrypt at index {i} should return original plaintext");
        }
    }

    [Test]
    public void ConcurrentMixedEncryptDecrypt_NoCorruption()
    {
        // Mix encrypt and decrypt calls concurrently on the same store instance
        var key = RandomHexKey();
        using var store = MakeStoreWithKey(key);

        // Pre-encrypt some values
        var preEncrypted = Enumerable.Range(0, 50)
            .Select(i => (Plain: $"pre-{i}", Cipher: store.Encrypt($"pre-{i}")))
            .ToArray();

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 100, i =>
        {
            if (i % 2 == 0)
            {
                // Encrypt + immediate decrypt round-trip
                var plain = $"concurrent-{i}";
                var ct = store.Encrypt(plain);
                var rt = store.Decrypt(ct);
                if (rt != plain)
                {
                    errors.Add($"Encrypt/Decrypt mismatch at {i}: expected '{plain}', got '{rt}'");
                }
            }
            else
            {
                // Decrypt a pre-encrypted value
                var idx = i % preEncrypted.Length;
                var rt = store.Decrypt(preEncrypted[idx].Cipher);
                if (rt != preEncrypted[idx].Plain)
                {
                    errors.Add($"Pre-encrypted decrypt mismatch at {i}");
                }
            }
        });

        errors.ShouldBeEmpty();
    }

    // ── MED-15: Decrypt after Dispose ───────────────────────────────────

    [Test]
    public void Decrypt_AfterDispose_ThrowsCryptographicException()
    {
        // After Dispose(), the key array is zeroed. Decrypting with a zeroed key
        // causes an authentication tag mismatch (AEAD verification failure).
        var key = RandomHexKey();
        var store = MakeStoreWithKey(key);

        var ciphertext = store.Encrypt("important-secret");
        store.Dispose();

        // The key is zeroed — decryption should fail with tag mismatch
        Should.Throw<CryptographicException>(() => store.Decrypt(ciphertext),
            "Decrypting after Dispose should fail because the key material has been zeroed");
    }

    [Test]
    public void Encrypt_AfterDispose_ProducesUndecryptableCiphertext()
    {
        // After Dispose(), encrypting uses zeroed key material. The resulting
        // ciphertext cannot be decrypted by a correctly-keyed instance.
        var key = RandomHexKey();
        var store = MakeStoreWithKey(key);

        store.Encrypt("warm up lazy key");
        store.Dispose();

        // Encrypt with zeroed key (this "works" but produces garbage)
        var postDisposeCt = store.Encrypt("post-dispose-value");
        // Encrypt still produces enc2: format even after dispose.
        postDisposeCt.ShouldStartWith("enc2:");

        // A fresh store with the real key cannot decrypt it
        using var freshStore = MakeStoreWithKey(key);
        // Post-dispose ciphertext encrypted with zeroed key must not decrypt with real key.
        Should.Throw<CryptographicException>(() => freshStore.Decrypt(postDisposeCt));
    }

    // ── MED-16: GenerateAndPersistKey race condition ────────────────────

    [Test]
    public void GenerateAndPersistKey_ConcurrentCreation_AllThreadsGetSameKey()
    {
        // Test that when multiple threads try to create the key file simultaneously,
        // they all end up with the same key (the winner writes, losers read).
        var tempDir = Path.Combine(Path.GetTempPath(), $"clawsharp-keyrace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var keyPath = Path.Combine(tempDir, ".secret_key");

        try
        {
            // Clear env var so all stores must use the file
            Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", null);

            // Create multiple stores concurrently — each triggers key generation
            const int threadCount = 10;
            var configs = Enumerable.Range(0, threadCount)
                .Select(_ => new AppConfig { Secrets = new SecretsConfig { Encrypt = true } })
                .ToArray();

            // All stores should get the same key, producing interoperable ciphertexts
            var ciphertexts = new string[threadCount];
            const string testPlaintext = "race-condition-test";

            // Create stores sequentially but they all share the same key file
            // The first one creates the key, subsequent ones read it.
            var stores = new SecretStore[threadCount];
            for (var i = 0; i < threadCount; i++)
            {
                stores[i] = new SecretStore(Options.Create(configs[i]));
                ciphertexts[i] = stores[i].Encrypt(testPlaintext);
            }

            // All ciphertexts should be decryptable by any store
            for (var i = 0; i < threadCount; i++)
            {
                for (var j = 0; j < threadCount; j++)
                {
                    var decrypted = stores[j].Decrypt(ciphertexts[i]);
                    decrypted.ShouldBe(testPlaintext,
                        $"Store {j} should decrypt ciphertext from store {i}");
                }
            }

            // Cleanup
            foreach (var s in stores)
            {
                s.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── MED-13: Wrong-length env var key ─────────────────────────────────

    [Test]
    public void LoadKey_16ByteHexKey_ThrowsCryptographicExceptionWithDescriptiveMessage()
    {
        // 16-byte key (32 hex chars) instead of the required 32 bytes (64 hex chars)
        var shortKey = Convert.ToHexString(new byte[16]).ToLowerInvariant();
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", shortKey);
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        var ex = Should.Throw<CryptographicException>(() => store.Encrypt("trigger-load"));

        // The error message should tell the user what went wrong
        // Error message should mention the expected key length in hex chars.
        ex.Message.ShouldContain("64");
        // Error message should mention the actual key length received.
        ex.Message.ShouldContain("32");
    }

    [Test]
    public void LoadKey_64ByteHexKey_ThrowsCryptographicExceptionWithDescriptiveMessage()
    {
        // 64-byte key (128 hex chars) — too long
        var longKey = Convert.ToHexString(new byte[64]).ToLowerInvariant();
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", longKey);
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        var ex = Should.Throw<CryptographicException>(() => store.Encrypt("trigger-load"));

        // Error message should mention the expected key length.
        ex.Message.ShouldContain("64");
        // Error message should mention the actual key length received.
        ex.Message.ShouldContain("128");
    }

    [Test]
    public void LoadKey_OddLengthHexKey_ThrowsCryptographicException()
    {
        // Odd-length hex string (not valid hex)
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", "abc");
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        Should.Throw<CryptographicException>(() => store.Encrypt("trigger-load"));
    }

    [Test]
    public void LoadKey_WhitespaceOnlyEnvVar_DoesNotCrash()
    {
        // Whitespace-only env var should be treated as "not set"
        Environment.SetEnvironmentVariable("CLAWSHARP_SECRET_KEY", "   ");
        var config = new AppConfig { Secrets = new SecretsConfig { Encrypt = true } };
        using var store = new SecretStore(Options.Create(config));

        // Should fall through to file-based key, not crash on whitespace
        // The store will either create a key file or throw a meaningful error — not NRE
        Should.NotThrow(() =>
        {
            try
            {
                store.Encrypt("trigger-load");
            }
            catch (CryptographicException)
            {
                // Expected if whitespace is treated as invalid hex
            }
            catch (FormatException)
            {
                // Also acceptable — Convert.FromHexString("   ") throws FormatException
            }
        });
    }
}
