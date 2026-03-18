using System.Security.Cryptography;
using System.Text;

namespace Clawsharp.Providers.Bedrock;

/// <summary>
///     Self-contained AWS Signature Version 4 request signer using only BCL crypto.
///     Signs HTTP requests for the Bedrock Runtime Converse API.
/// </summary>
internal static class AwsSigV4Signer
{
    /// <summary>
    ///     Computes AWS SigV4 headers for the given request parameters.
    /// </summary>
    /// <returns>Dictionary containing Authorization, x-amz-date, and x-amz-content-sha256 headers.</returns>
    internal static Dictionary<string, string> Sign(
        string method,
        Uri uri,
        string body,
        string accessKeyId,
        string secretAccessKey,
        string region,
        string service,
        DateTimeOffset now)
    {
        var date = now.UtcDateTime.ToString("yyyyMMdd");
        var datetime = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ");
        var bodyHash = HexHash(Encoding.UTF8.GetBytes(body));

        // Canonical URI — per AWS SigV4 spec, each path segment must be URI-encoded.
        // Uri.AbsolutePath is already percent-encoded by .NET for reserved characters, but
        // we must ensure each segment is properly encoded (e.g. '/' preserved, other specials encoded).
        // Re-encode each segment individually to handle edge cases (double-encode is not needed
        // for the canonical URI itself, only for the S3 path — Bedrock uses simple paths).
        var rawPath = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        var canonicalUri = CanonicalizeUri(rawPath);

        // Canonical query string — per AWS SigV4 spec, query string values must use %20 (not +).
        // Bedrock Converse uses no query params, but handle gracefully for other endpoints.
        var canonicalQueryString = CanonicalizeQueryString(uri.Query);

        // Signed headers (alphabetically sorted)
        const string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
        var host = uri.Host;
        if (!uri.IsDefaultPort)
        {
            host = $"{uri.Host}:{uri.Port}";
        }

        var canonicalRequest = string.Join('\n',
            method,
            canonicalUri,
            canonicalQueryString,
            $"content-type:application/json",
            $"host:{host}",
            $"x-amz-content-sha256:{bodyHash}",
            $"x-amz-date:{datetime}",
            "",
            signedHeaders,
            bodyHash);

        var credentialScope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            datetime,
            credentialScope,
            HexHash(Encoding.UTF8.GetBytes(canonicalRequest)));

        // Derive signing key: HMAC chain using stackalloc + static HashData to avoid HMAC allocations
        var prefixedKey = $"AWS4{secretAccessKey}";
        var keyByteCount = Encoding.UTF8.GetByteCount(prefixedKey);
        Span<byte> keyBytes = stackalloc byte[keyByteCount];
        Encoding.UTF8.GetBytes(prefixedKey, keyBytes);

        Span<byte> hmacBuf = stackalloc byte[32];
        HmacSha256Static(keyBytes, date, hmacBuf);
        HmacSha256Static(hmacBuf, region, hmacBuf);
        HmacSha256Static(hmacBuf, service, hmacBuf);
        HmacSha256Static(hmacBuf, "aws4_request", hmacBuf);

        Span<byte> sigBuf = stackalloc byte[32];
        HmacSha256Static(hmacBuf, stringToSign, sigBuf);
        var signature = Convert.ToHexStringLower(sigBuf);

        var authorization =
            $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        return new Dictionary<string, string>(3)
        {
            ["Authorization"] = authorization,
            ["x-amz-date"] = datetime,
            ["x-amz-content-sha256"] = bodyHash
        };
    }

    private static void HmacSha256Static(ReadOnlySpan<byte> key, ReadOnlySpan<char> data, Span<byte> destination)
    {
        var byteCount = Encoding.UTF8.GetByteCount(data);
        Span<byte> dataBytes = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(data, dataBytes);
        HMACSHA256.HashData(key, dataBytes, destination);
    }

    private static string HexHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return ToHex(hash);
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    /// <summary>
    /// Canonicalises the URI path per AWS SigV4: each path segment is URI-encoded,
    /// preserving '/' as segment separators.
    /// <para>
    /// <b>MED-52 verification:</b> <see cref="Uri.EscapeDataString"/> in .NET 5+ produces
    /// <b>uppercase</b> hex encoding (e.g. <c>%2F</c>, <c>%20</c>), which is correct for
    /// AWS SigV4 canonical URI. The hash hex (<see cref="ToHex"/>) is lowercase via
    /// <see cref="Convert.ToHexStringLower"/>, matching the SigV4 spec for hash values.
    /// </para>
    /// </summary>
    private static string CanonicalizeUri(string path)
    {
        if (path is "/" or "")
        {
            return "/";
        }

        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            // Uri.EscapeDataString encodes everything except unreserved chars (RFC 3986).
            // We first unescape (in case .NET already encoded) then re-encode to normalize.
            // .NET 5+ guarantees uppercase hex (%XX), which AWS SigV4 requires for the canonical URI.
            segments[i] = Uri.EscapeDataString(Uri.UnescapeDataString(segments[i]));
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// Canonicalises the query string per AWS SigV4: parameters sorted by key,
    /// values use %20 instead of +, keys and values are URI-encoded.
    /// </summary>
    private static string CanonicalizeQueryString(string query)
    {
        if (query.Length <= 1)
        {
            return "";
        }

        // Strip leading '?'
        var raw = query[1..];
        var pairs = raw.Split('&')
                       .Select(p =>
                       {
                           var eq = p.IndexOf('=');
                           if (eq < 0)
                           {
                               return (Key: Uri.EscapeDataString(Uri.UnescapeDataString(p)), Value: "");
                           }

                           var key = Uri.EscapeDataString(Uri.UnescapeDataString(p[..eq]));
                           var val = Uri.EscapeDataString(Uri.UnescapeDataString(p[(eq + 1)..]));
                           return (Key: key, Value: val);
                       })
                       .OrderBy(p => p.Key, StringComparer.Ordinal)
                       .ThenBy(p => p.Value, StringComparer.Ordinal);

        return string.Join('&', pairs.Select(p => $"{p.Key}={p.Value}"));
    }
}