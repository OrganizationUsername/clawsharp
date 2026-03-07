using System.Text.RegularExpressions;

namespace Clawsharp.Security;

/// <summary>
///     Scans LLM output for potential credential or secret leaks before delivery to channels.
///     Ported from zeroclaw/src/security/leak_detector.rs.
/// </summary>
public static partial class LeakDetector
{
    private const double GenericSecretSensitivityThreshold = 0.5;

    private const int EntropyTokenMinLen = 24;

    private const double HighEntropyBaseline = 4.2;

    /// <summary>Scans content for credential leaks.</summary>
    /// <param name="content">The LLM output to scan.</param>
    /// <param name="sensitivity">Detection sensitivity 0.0–1.0 (default 0.7).</param>
    /// <returns>Scan result with detected patterns and redacted content.</returns>
    public static LeakScanResult Scan(string content, double sensitivity = 0.7)
    {
        var patterns = new List<string>();
        var redacted = content;

        CheckApiKeys(ref redacted, content, patterns);
        CheckAwsCredentials(ref redacted, content, patterns);
        CheckPrivateKeys(ref redacted, content, patterns);
        CheckJwtTokens(ref redacted, content, patterns);
        CheckDatabaseUrls(ref redacted, content, patterns);
        if (sensitivity > GenericSecretSensitivityThreshold)
        {
            CheckGenericSecrets(ref redacted, content, patterns);
            CheckHighEntropyTokens(ref redacted, content, patterns, sensitivity);
        }

        return new LeakScanResult(patterns.Count == 0, patterns, redacted);
    }

    // ── Structural patterns (fire at any sensitivity) ──────────────────────

    [GeneratedRegex(@"sk_(live|test)_[a-zA-Z0-9]{24,}", RegexOptions.CultureInvariant)]
    private static partial Regex StripeKeyRegex();

    [GeneratedRegex(@"sk-[a-zA-Z0-9]{20,}T3BlbkFJ[a-zA-Z0-9]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiClassicKeyRegex();

    [GeneratedRegex(@"sk-[a-zA-Z0-9]{48,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiStyleKeyRegex();

    [GeneratedRegex(@"sk-ant-[a-zA-Z0-9\-_]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex AnthropicKeyRegex();

    [GeneratedRegex(@"AIza[a-zA-Z0-9_\-]{35}", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleApiKeyRegex();

    [GeneratedRegex(@"gh[pousr]_[a-zA-Z0-9]{36,}", RegexOptions.CultureInvariant)]
    private static partial Regex GithubTokenRegex();

    [GeneratedRegex(@"github_pat_[a-zA-Z0-9_]{22,}", RegexOptions.CultureInvariant)]
    private static partial Regex GithubPatRegex();

    [GeneratedRegex("""api[_\-]?key[=:]\s*['""]*[a-zA-Z0-9_\-]{20,}""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericApiKeyRegex();

    [GeneratedRegex(@"AKIA[A-Z0-9]{16}", RegexOptions.CultureInvariant)]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex("""aws[_\-]?secret[_\-]?access[_\-]?key[=:]\s*['""]*[a-zA-Z0-9/+=]{40}""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AwsSecretKeyRegex();

    [GeneratedRegex(@"eyJ[a-zA-Z0-9_\-]*\.eyJ[a-zA-Z0-9_\-]*\.[a-zA-Z0-9_\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"postgres(ql)?://[^:]+:[^@]+@\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PostgresUrlRegex();

    [GeneratedRegex(@"mysql://[^:]+:[^@]+@\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MysqlUrlRegex();

    [GeneratedRegex(@"mongodb(\+srv)?://[^:]+:[^@]+@\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MongoUrlRegex();

    [GeneratedRegex(@"redis://[^:]+:[^@]+@\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RedisUrlRegex();

    // ── Sensitivity-gated patterns ─────────────────────────────────────────

    [GeneratedRegex("""(?i)password[=:]\s*['""]*[^\s'""]{8,}""", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex("""(?i)secret[=:]\s*['""]*[a-zA-Z0-9_\-]{16,}""", RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();

    [GeneratedRegex("""(?i)token[=:]\s*['""]*[a-zA-Z0-9_.\-]{20,}""", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    // ── Check methods ──────────────────────────────────────────────────────

    private static void CheckApiKeys(ref string redacted, string original, List<string> patterns)
    {
        RedactIfMatch(StripeKeyRegex(), ref redacted, original, patterns, "Stripe secret key", "[REDACTED_API_KEY]");
        RedactIfMatch(OpenAiClassicKeyRegex(), ref redacted, original, patterns, "OpenAI API key", "[REDACTED_API_KEY]");
        RedactIfMatch(OpenAiStyleKeyRegex(), ref redacted, original, patterns, "OpenAI-style API key", "[REDACTED_API_KEY]");
        RedactIfMatch(AnthropicKeyRegex(), ref redacted, original, patterns, "Anthropic API key", "[REDACTED_API_KEY]");
        RedactIfMatch(GoogleApiKeyRegex(), ref redacted, original, patterns, "Google API key", "[REDACTED_API_KEY]");
        RedactIfMatch(GithubTokenRegex(), ref redacted, original, patterns, "GitHub token", "[REDACTED_API_KEY]");
        RedactIfMatch(GithubPatRegex(), ref redacted, original, patterns, "GitHub PAT", "[REDACTED_API_KEY]");
        RedactIfMatch(GenericApiKeyRegex(), ref redacted, original, patterns, "Generic API key", "[REDACTED_API_KEY]");
    }

    private static void CheckAwsCredentials(ref string redacted, string original, List<string> patterns)
    {
        RedactIfMatch(AwsAccessKeyRegex(), ref redacted, original, patterns, "AWS Access Key ID", "[REDACTED_AWS_CREDENTIAL]");
        RedactIfMatch(AwsSecretKeyRegex(), ref redacted, original, patterns, "AWS Secret Access Key", "[REDACTED_AWS_CREDENTIAL]");
    }

    private static void CheckJwtTokens(ref string redacted, string original, List<string> patterns)
        => RedactIfMatch(JwtRegex(), ref redacted, original, patterns, "JWT token", "[REDACTED_JWT]");

    private static void CheckDatabaseUrls(ref string redacted, string original, List<string> patterns)
    {
        RedactIfMatch(PostgresUrlRegex(), ref redacted, original, patterns, "PostgreSQL connection URL", "[REDACTED_DATABASE_URL]");
        RedactIfMatch(MysqlUrlRegex(), ref redacted, original, patterns, "MySQL connection URL", "[REDACTED_DATABASE_URL]");
        RedactIfMatch(MongoUrlRegex(), ref redacted, original, patterns, "MongoDB connection URL", "[REDACTED_DATABASE_URL]");
        RedactIfMatch(RedisUrlRegex(), ref redacted, original, patterns, "Redis connection URL", "[REDACTED_DATABASE_URL]");
    }

    private static readonly (string Begin, string End, string Label)[] PrivateKeyPatterns =
    [
        ("-----BEGIN RSA PRIVATE KEY-----", "-----END RSA PRIVATE KEY-----", "RSA private key"),
        ("-----BEGIN EC PRIVATE KEY-----", "-----END EC PRIVATE KEY-----", "EC private key"),
        ("-----BEGIN PRIVATE KEY-----", "-----END PRIVATE KEY-----", "Private key"),
        ("-----BEGIN OPENSSH PRIVATE KEY-----", "-----END OPENSSH PRIVATE KEY-----", "OpenSSH private key"),
    ];

    private static void CheckPrivateKeys(ref string redacted, string original, List<string> patterns)
    {
        foreach (var (begin, end, label) in PrivateKeyPatterns)
        {
            if (!original.Contains(begin, StringComparison.Ordinal) ||
                !original.Contains(end, StringComparison.Ordinal))
            {
                continue;
            }

            patterns.Add(label);
            var start = redacted.IndexOf(begin, StringComparison.Ordinal);
            var finish = redacted.IndexOf(end, StringComparison.Ordinal);
            if (start >= 0 && finish >= 0)
            {
                redacted = string.Concat(redacted[..start], "[REDACTED_PRIVATE_KEY]", redacted[(finish + end.Length)..]);
            }
        }
    }

    private static void CheckGenericSecrets(ref string redacted, string original, List<string> patterns)
    {
        RedactIfMatch(PasswordRegex(), ref redacted, original, patterns, "Password in config", "[REDACTED_SECRET]");
        RedactIfMatch(SecretRegex(), ref redacted, original, patterns, "Secret value", "[REDACTED_SECRET]");
        RedactIfMatch(TokenRegex(), ref redacted, original, patterns, "Token value", "[REDACTED_SECRET]");
    }

    private static void CheckHighEntropyTokens(ref string redacted, string original, List<string> patterns, double sensitivity)
    {
        var threshold = Math.Clamp(HighEntropyBaseline + (sensitivity - 0.5) * 0.6, 3.9, 4.8);
        var flagged = false;

        foreach (var token in ExtractCandidateTokens(original))
        {
            if (token.Length < EntropyTokenMinLen)
            {
                continue;
            }

            if (!token.Any(char.IsAsciiLetter) || !token.Any(char.IsAsciiDigit))
            {
                continue;
            }

            var entropy = ShannonEntropy(token);
            if (entropy >= threshold)
            {
                flagged = true;
                redacted = redacted.Replace(token, "[REDACTED_HIGH_ENTROPY_TOKEN]", StringComparison.Ordinal);
            }
        }

        if (flagged)
        {
            patterns.Add("High-entropy token (possible encoded secret)");
        }
    }

    private static IEnumerable<string> ExtractCandidateTokens(string content)
    {
        var start = 0;
        for (var i = 0; i <= content.Length; i++)
        {
            var isSep = i == content.Length ||
                        !(char.IsAsciiLetterOrDigit(content[i]) || content[i] is '_' or '-' or '+' or '/' or '=');
            if (isSep && i > start)
            {
                yield return content[start..i];
                start = i + 1;
            }
            else if (isSep)
            {
                start = i + 1;
            }
        }
    }

    private static double ShannonEntropy(string token)
    {
        if (token.Length == 0)
        {
            return 0;
        }

        Span<int> counts = stackalloc int[128];
        var inRange = 0;
        foreach (var c in token)
        {
            if (c < 128)
            {
                counts[c]++;
                inRange++;
            }
        }

        if (inRange == 0)
        {
            return 0;
        }

        var len = (double)inRange;
        var entropy = 0.0;
        foreach (var count in counts)
        {
            if (count == 0)
            {
                continue;
            }

            var p = count / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static void RedactIfMatch(Regex regex, ref string redacted, string original, List<string> patterns, string label,
                                      string replacement)
    {
        if (!regex.IsMatch(original))
        {
            return;
        }

        patterns.Add(label);
        redacted = regex.Replace(redacted, replacement);
    }
}

/// <summary>Result of a leak detection scan.</summary>
public sealed record LeakScanResult(
    bool IsClean,
    IReadOnlyList<string> Patterns,
    string Redacted
);