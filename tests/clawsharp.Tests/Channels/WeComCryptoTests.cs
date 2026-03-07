using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Clawsharp.Channels.WeCom;

namespace Clawsharp.Tests.Channels;

[TestFixture]
public sealed class WeComCryptoTests
{
    // A well-known 43-character base64 encoding key for testing.
    // Base64-decodes to exactly 32 bytes when "=" is appended.
    private const string TestEncodingAesKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";

    [Test]
    public void DeriveAesKey_ValidKey_Returns32Bytes()
    {
        var key = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        key.Length.ShouldBe(32);
    }

    [Test]
    public void DeriveAesKey_WrongLength_Throws()
    {
        Should.Throw<ArgumentException>(() => WeComCrypto.DeriveAesKey("tooshort"));
    }

    [Test]
    public void DeriveAesKey_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() => WeComCrypto.DeriveAesKey(null!));
    }

    [Test]
    public void VerifySignature_CorrectSignature_ReturnsTrue()
    {
        var token = "test_token";
        var timestamp = "1700000000";
        var nonce = "random123";
        var encrypt = "some_encrypted_content";

        // Compute expected: sort [token, timestamp, nonce, encrypt], concat, SHA1
        string[] parts = [token, timestamp, nonce, encrypt];
        Array.Sort(parts, StringComparer.Ordinal);
        var combined = string.Concat(parts);
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(combined));
        var expected = Convert.ToHexStringLower(hash);

        WeComCrypto.VerifySignature(token, timestamp, nonce, encrypt, expected).ShouldBeTrue();
    }

    [Test]
    public void VerifySignature_WrongSignature_ReturnsFalse()
    {
        WeComCrypto.VerifySignature("token", "ts", "nonce", "enc", "deadbeef").ShouldBeFalse();
    }

    [Test]
    public void VerifySignature_CaseInsensitive()
    {
        var token = "tok";
        var timestamp = "12345";
        var nonce = "abc";
        var encrypt = "xyz";

        string[] parts = [token, timestamp, nonce, encrypt];
        Array.Sort(parts, StringComparer.Ordinal);
        var combined = string.Concat(parts);
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(combined));
        var expectedLower = Convert.ToHexStringLower(hash);
        var expectedUpper = expectedLower.ToUpperInvariant();

        WeComCrypto.VerifySignature(token, timestamp, nonce, encrypt, expectedUpper).ShouldBeTrue();
    }

    [Test]
    public void Decrypt_RoundTrip_ReturnsOriginalMessage()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        var originalMessage = """{"msgtype":"text","text":{"content":"hello world"}}""";
        var receiverId = ""; // AI Bot uses empty receiverId

        var encrypted = Encrypt(aesKey, originalMessage, receiverId);
        var decrypted = WeComCrypto.Decrypt(aesKey, encrypted);

        decrypted.ShouldBe(originalMessage);
    }

    [Test]
    public void Decrypt_WithReceiverId_ReturnsOnlyMessage()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        var originalMessage = """{"test":"value"}""";
        var receiverId = "some_corp_id";

        var encrypted = Encrypt(aesKey, originalMessage, receiverId);
        var decrypted = WeComCrypto.Decrypt(aesKey, encrypted);

        decrypted.ShouldBe(originalMessage);
    }

    [Test]
    public void Decrypt_EmptyMessage_Succeeds()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        var originalMessage = "";

        var encrypted = Encrypt(aesKey, originalMessage, "");
        var decrypted = WeComCrypto.Decrypt(aesKey, encrypted);

        decrypted.ShouldBe("");
    }

    [Test]
    public void Decrypt_LargeMessage_Succeeds()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        var originalMessage = new string('A', 10000);

        var encrypted = Encrypt(aesKey, originalMessage, "");
        var decrypted = WeComCrypto.Decrypt(aesKey, encrypted);

        decrypted.ShouldBe(originalMessage);
    }

    [Test]
    public void Decrypt_InvalidBase64_Throws()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        Should.Throw<FormatException>(() => WeComCrypto.Decrypt(aesKey, "not_valid_base64!!!"));
    }

    [Test]
    public void Decrypt_CorruptedData_Throws()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        // Encrypt something valid, then corrupt it
        var encrypted = Encrypt(aesKey, "test", "");
        var bytes = Convert.FromBase64String(encrypted);
        bytes[bytes.Length / 2] ^= 0xFF; // Flip bits in the middle
        var corrupted = Convert.ToBase64String(bytes);

        // Should throw CryptographicException due to bad padding or length
        Should.Throw<CryptographicException>(() => WeComCrypto.Decrypt(aesKey, corrupted));
    }

    [Test]
    public void Decrypt_UnicodeMessage_Succeeds()
    {
        var aesKey = WeComCrypto.DeriveAesKey(TestEncodingAesKey);
        var originalMessage = """{"text":{"content":"Hello! 你好世界 🌍"}}""";

        var encrypted = Encrypt(aesKey, originalMessage, "");
        var decrypted = WeComCrypto.Decrypt(aesKey, encrypted);

        decrypted.ShouldBe(originalMessage);
    }

    /// <summary>
    /// Helper: encrypts a message using the WeCom AES-256-CBC protocol
    /// (32-byte PKCS7 padding, 16 random bytes prefix, 4-byte big-endian length, receiverId suffix).
    /// </summary>
    private static string Encrypt(byte[] aesKey, string message, string receiverId)
    {
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var receiverBytes = Encoding.UTF8.GetBytes(receiverId);

        // Build plaintext: [16 random] [4 bytes msg_len BE] [message] [receiverId]
        var random = RandomNumberGenerator.GetBytes(16);
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, msgBytes.Length);

        var plaintext = new byte[16 + 4 + msgBytes.Length + receiverBytes.Length];
        random.CopyTo(plaintext, 0);
        lenBytes.CopyTo(plaintext, 16);
        msgBytes.CopyTo(plaintext, 20);
        receiverBytes.CopyTo(plaintext, 20 + msgBytes.Length);

        // Non-standard PKCS7 padding with 32-byte block size
        var padLen = 32 - (plaintext.Length % 32);
        if (padLen == 0) padLen = 32;
        var padded = new byte[plaintext.Length + padLen];
        plaintext.CopyTo(padded, 0);
        for (var i = 0; i < padLen; i++)
        {
            padded[plaintext.Length + i] = (byte)padLen;
        }

        // AES-256-CBC encrypt
        var iv = aesKey.AsSpan(0, 16).ToArray();
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(padded, 0, padded.Length);
        return Convert.ToBase64String(encrypted);
    }
}