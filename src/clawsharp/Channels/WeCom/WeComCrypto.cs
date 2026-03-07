using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Clawsharp.Channels.WeCom;

/// <summary>
/// WeCom AI Bot AES-256-CBC encryption/decryption and signature verification.
/// Uses a non-standard 32-byte PKCS7 block size (WeCom protocol requirement).
/// </summary>
internal static class WeComCrypto
{
    /// <summary>
    /// Derives the 32-byte AES key from the 43-character <c>EncodingAesKey</c> config value.
    /// Appends <c>"="</c> to make a valid 44-character base64 string, then decodes.
    /// </summary>
    public static byte[] DeriveAesKey(string encodingAesKey)
    {
        ArgumentNullException.ThrowIfNull(encodingAesKey);
        if (encodingAesKey.Length != 43)
        {
            throw new ArgumentException("EncodingAesKey must be exactly 43 characters.", nameof(encodingAesKey));
        }

        return Convert.FromBase64String(encodingAesKey + "=");
    }

    /// <summary>
    /// Verifies the WeCom message signature.
    /// Sorts <c>[token, timestamp, nonce, encrypt]</c> alphabetically,
    /// concatenates them, computes SHA-1, and compares the hex digest.
    /// </summary>
    public static bool VerifySignature(string token, string timestamp, string nonce, string encrypt, string expectedSignature)
    {
        string[] parts = [token, timestamp, nonce, encrypt];
        Array.Sort(parts, StringComparer.Ordinal);
        var combined = string.Concat(parts);

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(combined));
        var actual = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant()));
    }

    /// <summary>
    /// Decrypts a WeCom AES-256-CBC encrypted base64 string.
    /// </summary>
    /// <param name="aesKey">32-byte AES key derived from <see cref="DeriveAesKey"/>.</param>
    /// <param name="encryptedBase64">Base64-encoded encrypted payload.</param>
    /// <returns>The decrypted message string (JSON for AI Bot messages).</returns>
    public static string Decrypt(byte[] aesKey, string encryptedBase64)
    {
        var encrypted = Convert.FromBase64String(encryptedBase64);
        var iv = aesKey.AsSpan(0, 16).ToArray();

        byte[] decrypted;
        using (var aes = Aes.Create())
        {
            aes.Key = aesKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None; // We handle non-standard 32-byte PKCS7 manually
            aes.BlockSize = 128; // AES block size is always 128 bits

            using var decryptor = aes.CreateDecryptor();
            decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }

        // Remove non-standard PKCS7 padding (32-byte block size).
        // Last byte indicates the number of padding bytes.
        var padLen = decrypted[^1];
        if (padLen is < 1 or > 32)
        {
            throw new CryptographicException("Invalid WeCom PKCS7 padding byte value.");
        }

        var unpaddedLen = decrypted.Length - padLen;
        if (unpaddedLen < 20)
        {
            throw new CryptographicException("Decrypted WeCom payload too short.");
        }

        // Structure: [16 bytes random] [4 bytes msg_len (big-endian)] [msg_len bytes message] [receiveid bytes]
        var msgLen = BinaryPrimitives.ReadInt32BigEndian(decrypted.AsSpan(16, 4));
        if (msgLen < 0 || 20 + msgLen > unpaddedLen)
        {
            throw new CryptographicException("Invalid WeCom message length field.");
        }

        return Encoding.UTF8.GetString(decrypted, 20, msgLen);
    }
}