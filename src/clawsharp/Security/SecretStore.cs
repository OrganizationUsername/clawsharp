using System.Security.Cryptography;
using System.Text;
using Clawsharp.Config;
using Microsoft.Extensions.Options;

namespace Clawsharp.Security;

/// <summary>
/// Encrypts and decrypts secret configuration values using ChaCha20-Poly1305 AEAD.
/// Format: "enc2:&lt;hex(nonce || ciphertext || tag)&gt;" where nonce=12 bytes, tag=16 bytes.
/// Key file: ~/.clawsharp/.secret_key (32-byte key, hex-encoded, chmod 0600 on Unix).
/// </summary>
public sealed class SecretStore(IOptions<AppConfig> options) : IDisposable
{
    private const int KeyLen = 32;

    private const int NonceLen = 12;

    private const int TagLen = 16;

    private const string Prefix = "enc2:";

    private readonly bool _enabled = options.Value.Secrets?.Encrypt ?? true;

    private readonly Lazy<byte[]> _key = new(LoadOrCreateKey);

    /// <summary>
    /// Encrypt a plaintext value. Returns the plaintext unchanged if encryption is disabled,
    /// the value is null/empty, or already encrypted.
    /// </summary>
    public string Encrypt(string? value)
    {
        if (!_enabled || string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value ?? "";
        }

        var key = _key.Value;
        var nonce = new byte[NonceLen];
        RandomNumberGenerator.Fill(nonce);

        var plainBytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[TagLen];

            using var aead = new ChaCha20Poly1305(key);
            aead.Encrypt(nonce, plainBytes, cipherBytes, tag);

            var blob = new byte[NonceLen + cipherBytes.Length + TagLen];
            nonce.CopyTo(blob, 0);
            cipherBytes.CopyTo(blob, NonceLen);
            tag.CopyTo(blob, NonceLen + cipherBytes.Length);

            return Prefix + Convert.ToHexString(blob).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// Decrypt a value. Plaintext (no "enc2:" prefix) is returned as-is.
    /// </summary>
    public string Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value; // plaintext passthrough
        }

        return DecryptInternal(value.AsSpan(Prefix.Length));
    }

    private string DecryptInternal(ReadOnlySpan<char> hexBlob)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromHexString(hexBlob);
        }
        catch
        {
            throw new CryptographicException("Invalid enc2 format: not valid hex.");
        }

        if (blob.Length < NonceLen + TagLen)
        {
            throw new CryptographicException("Invalid enc2 format: blob too short.");
        }

        var nonce = blob[..NonceLen];
        var tag = blob[^TagLen..];
        var cipherBytes = blob[NonceLen..^TagLen];
        var plainBytes = new byte[cipherBytes.Length];

        var key = _key.Value;
        using var aead = new ChaCha20Poly1305(key);
        aead.Decrypt(nonce, cipherBytes, tag, plainBytes); // throws AuthenticationTagMismatchException on tamper

        try
        {
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// MED-03: Zero the encryption key material when the store is disposed.
    /// </summary>
    public void Dispose()
    {
        if (_key.IsValueCreated)
        {
            CryptographicOperations.ZeroMemory(_key.Value);
        }
    }

    private static byte[] LoadOrCreateKey()
    {
        // Priority 1: environment variable — CI/CD, Kubernetes, inline container override.
        if (TryLoadFromEnvironment(out var envKey))
        {
            return envKey;
        }

        // Priority 2: Docker native secrets file.
        if (TryLoadFromDockerSecret(out var dockerKey))
        {
            return dockerKey;
        }

        // Priority 3: local key file — ~/.clawsharp/.secret_key (development / bare-metal).
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clawsharp");
        Directory.CreateDirectory(dir);
        var keyPath = Path.Combine(dir, ".secret_key");

        if (TryLoadFromFile(keyPath, out var fileKey))
        {
            return fileKey;
        }

        return GenerateAndPersistKey(keyPath);
    }

    /// <summary>
    /// Attempt to load the encryption key from the CLAWSHARP_SECRET_KEY environment variable.
    /// Generate once: <c>openssl rand -hex 32</c>
    /// </summary>
    private static bool TryLoadFromEnvironment(out byte[] key)
    {
        key = [];
        var envKey = Environment.GetEnvironmentVariable("CLAWSHARP_SECRET_KEY");
        if (string.IsNullOrEmpty(envKey))
        {
            return false;
        }

        try
        {
            key = Convert.FromHexString(envKey.Trim());
        }
        catch
        {
            throw new CryptographicException("CLAWSHARP_SECRET_KEY is not valid hex.");
        }

        if (key.Length != KeyLen)
        {
            throw new CryptographicException(
                $"CLAWSHARP_SECRET_KEY must be {KeyLen * 2} hex chars (got {envKey.Trim().Length}).");
        }

        return true;
    }

    /// <summary>
    /// Attempt to load the encryption key from a Docker native secret file.
    /// Mounted at /run/secrets/clawsharp_secret_key when the compose 'secrets:' block is used.
    /// More secure than env vars — not visible in 'docker inspect' and not inherited by child processes.
    /// Setup: <c>openssl rand -hex 32 &gt; clawsharp_secret_key.txt</c> (add to .gitignore)
    /// Then reference it in compose: <c>secrets: clawsharp_secret_key: file: ./clawsharp_secret_key.txt</c>
    /// </summary>
    private static bool TryLoadFromDockerSecret(out byte[] key)
    {
        key = [];
        const string dockerSecretPath = "/run/secrets/clawsharp_secret_key";
        if (!File.Exists(dockerSecretPath))
        {
            return false;
        }

        var secretHex = File.ReadAllText(dockerSecretPath).Trim();
        try
        {
            key = Convert.FromHexString(secretHex);
        }
        catch
        {
            throw new CryptographicException($"Docker secret at '{dockerSecretPath}' is not valid hex.");
        }

        if (key.Length != KeyLen)
        {
            throw new CryptographicException(
                $"Docker secret at '{dockerSecretPath}' must be {KeyLen * 2} hex chars.");
        }

        return true;
    }

    /// <summary>Attempt to load an existing key from a local file.</summary>
    private static bool TryLoadFromFile(string keyPath, out byte[] key)
    {
        key = [];
        if (!File.Exists(keyPath))
        {
            return false;
        }

        var hex = File.ReadAllText(keyPath).Trim();
        key = Convert.FromHexString(hex);
        if (key.Length != KeyLen)
        {
            throw new CryptographicException($"Secret key file at '{keyPath}' is invalid (expected {KeyLen * 2} hex chars).");
        }

        return true;
    }

    /// <summary>Generate a new random key and persist it atomically to disk.</summary>
    /// <remarks>
    /// Uses <c>File.Move(overwrite: false)</c> for atomic creation. If two processes race,
    /// the loser catches <see cref="IOException"/>, cleans up its temp file, and reads the
    /// key that the winning process persisted — preventing a permanent <see cref="Lazy{T}"/>
    /// fault that would make all subsequent <see cref="Encrypt"/>/<see cref="Decrypt"/> calls fail.
    /// </remarks>
    private static byte[] GenerateAndPersistKey(string keyPath)
    {
        var newKey = new byte[KeyLen];
        RandomNumberGenerator.Fill(newKey);
        var newHex = Convert.ToHexString(newKey).ToLowerInvariant();

        var tmpPath = keyPath + ".tmp";
        File.WriteAllText(tmpPath, newHex);
        SetKeyFilePermissions(tmpPath);

        try
        {
            File.Move(tmpPath, keyPath, overwrite: false);
        }
        catch (IOException)
        {
            // Another process won the race — discard our key material and temp file.
            CryptographicOperations.ZeroMemory(newKey);
            try { File.Delete(tmpPath); } catch { /* best-effort cleanup */ }

            if (TryLoadFromFile(keyPath, out var existingKey))
            {
                return existingKey;
            }

            // Key file doesn't exist either — something else went wrong (e.g. permissions).
            throw;
        }

        return newKey;
    }

    private static void SetKeyFilePermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            // chmod 0600: owner read+write only
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            // Non-fatal -- best effort on platforms where this may fail, but log so the operator knows
            Console.Error.WriteLine($"[SecretStore] Warning: failed to set file permissions on '{path}': {ex.Message}");
        }
    }
}