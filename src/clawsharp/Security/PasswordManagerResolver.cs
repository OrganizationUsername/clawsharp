using System.Diagnostics;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Config.Security;

namespace Clawsharp.Security;

/// <summary>
/// Resolves password-manager secret references embedded in config values.
///
/// Supported URI schemes:
///   op://vault/item/field          — 1Password (resolved via `op read`)
///   bws:&lt;uuid&gt;                     — Bitwarden Secrets Manager (resolved via `bws secret get`)
///
/// Usage: store the reference URI directly as a config field value, e.g.
///   "apiKey": "op://prod/openai/credential"
///   "apiKey": "bws:be8e0ad8-d545-4017-a55a-b02f014d4158"
///
/// References are resolved in-memory at startup and never written back to disk.
/// They are transparent to the rest of the application — after resolution the field
/// contains a plain string indistinguishable from any other decrypted secret.
/// </summary>
internal static class PasswordManagerResolver
{
    private const string OpPrefix = "op://";

    private const string BwsPrefix = "bws:";

    /// <summary>
    /// Returns true if <paramref name="value"/> is a password manager reference
    /// (starts with "op://" or "bws:").
    /// </summary>
    internal static bool IsReference(string? value)
        => value is not null
           && (value.StartsWith(OpPrefix, StringComparison.Ordinal)
               || value.StartsWith(BwsPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Resolves an op:// or bws: reference to its plaintext value.
    /// Non-reference values are returned unchanged.
    /// Throws <see cref="InvalidOperationException"/> if the CLI is unavailable,
    /// the auth token is missing, or the secret cannot be found.
    /// </summary>
    internal static string Resolve(string value, SecretsConfig config)
    {
        if (value.StartsWith(OpPrefix, StringComparison.Ordinal))
        {
            return ResolveOnePassword(value, config.OnePassword ?? new OnePasswordConfig());
        }

        if (value.StartsWith(BwsPrefix, StringComparison.Ordinal))
        {
            return ResolveBitwarden(value[BwsPrefix.Length..], config.Bitwarden ?? new BitwardenConfig());
        }

        return value;
    }

    // ── 1Password ─────────────────────────────────────────────────────────────────

    private static string ResolveOnePassword(string reference, OnePasswordConfig cfg)
    {
        var token = Environment.GetEnvironmentVariable(cfg.TokenEnvVar);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"1Password reference '{reference}' found in config but {cfg.TokenEnvVar} is not set. " +
                $"Create a service account at https://my.1password.com/developer-tools/infrastructure-secrets/serviceaccount " +
                $"and export {cfg.TokenEnvVar}=<token>.");
        }

        var (stdout, stderr, exit) = RunProcess(cfg.CliBinary, ["read", reference], cfg.TimeoutSeconds);

        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"1Password: op read failed for '{reference}' (exit {exit}): {stderr.Trim()}");
        }

        var result = stdout.Trim();
        if (result.Length == 0)
        {
            throw new InvalidOperationException(
                $"1Password: op read returned an empty value for '{reference}'.");
        }

        return result;
    }

    // ── Bitwarden Secrets Manager ─────────────────────────────────────────────────

    private static string ResolveBitwarden(string secretId, BitwardenConfig cfg)
    {
        var token = Environment.GetEnvironmentVariable(cfg.TokenEnvVar);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"Bitwarden reference 'bws:{secretId}' found in config but {cfg.TokenEnvVar} is not set. " +
                $"Create a machine account at https://vault.bitwarden.com/#/sm " +
                $"and export {cfg.TokenEnvVar}=<access-token>.");
        }

        var (stdout, stderr, exit) = RunProcess(
            cfg.CliBinary, ["secret", "get", secretId, "--output", "json"], cfg.TimeoutSeconds);

        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"Bitwarden: bws secret get failed for '{secretId}' (exit {exit}): {stderr.Trim()}");
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var val = doc.RootElement.GetProperty("value").GetString();
            if (val is null)
            {
                throw new InvalidOperationException(
                    $"Bitwarden: secret '{secretId}' has a null value.");
            }

            return val;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Bitwarden: failed to parse bws response for secret '{secretId}': {ex.Message}");
        }
    }

    // ── Process helper ─────────────────────────────────────────────────────────────

    private static readonly string[] AllowedBinaries = ["op", "bws"];

    /// <summary>
    /// Directories from which absolute CLI binary paths are allowed.
    /// Only standard system binary directories are permitted to prevent execution of
    /// attacker-controlled binaries from writable locations.
    /// </summary>
    private static readonly string[] AllowedBinaryDirectories =
    [
        "/usr/bin",
        "/usr/local/bin",
        "/bin",
        "/snap/bin",
        "/opt/homebrew/bin", // macOS Homebrew (Apple Silicon)
        "/home/linuxbrew/.linuxbrew/bin", // Linux Homebrew
    ];

    private static (string Stdout, string Stderr, int ExitCode) RunProcess(
        string binary, string[] arguments, int timeoutSeconds)
    {
        // Validate the binary name to prevent arbitrary command execution.
        // Only the filename portion is checked so that full paths like /usr/bin/op are allowed.
        var binaryFileName = Path.GetFileNameWithoutExtension(binary);
        if (!AllowedBinaries.Contains(binaryFileName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Invalid password manager CLI binary '{binary}'. " +
                $"Only the following binaries are allowed: {string.Join(", ", AllowedBinaries)}");
        }

        // If a path separator is present, require an absolute path and validate the directory.
        // Bare names like "op" are fine — resolved from PATH at runtime.
        if (binary.Contains(Path.DirectorySeparatorChar) || binary.Contains(Path.AltDirectorySeparatorChar))
        {
            if (!Path.IsPathFullyQualified(binary))
            {
                throw new InvalidOperationException(
                    $"Password manager CLI binary path '{binary}' must be either a bare name (resolved from PATH) " +
                    $"or an absolute path. Relative paths are not allowed.");
            }

            // Reject path traversal sequences
            var resolved = Path.GetFullPath(binary);
            if (resolved != binary || binary.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Password manager CLI binary path '{binary}' contains path traversal sequences and is not allowed.");
            }

            // Validate the containing directory is in the allowlist
            var directory = Path.GetDirectoryName(resolved);
            if (directory is null || !AllowedBinaryDirectories.Contains(directory, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Password manager CLI binary '{binary}' is not in an allowed directory. " +
                    $"Allowed directories: {string.Join(", ", AllowedBinaryDirectories)}. " +
                    $"Use a bare name (e.g., 'op') to resolve from PATH instead.");
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var proc = new Process { StartInfo = startInfo };

        // Inherits the full parent environment so PATH, HOME, and the auth token
        // env var (OP_SERVICE_ACCOUNT_TOKEN / BWS_ACCESS_TOKEN) are all available.

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start '{binary}': {ex.Message}. " +
                $"Is it installed and on PATH? " +
                $"1Password: https://developer.1password.com/docs/cli/get-started/ " +
                $"Bitwarden: https://bitwarden.com/help/secrets-manager-cli/");
        }

        // Read both streams before waiting to avoid deadlock on large output.
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();

        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                /* best effort */
            }

            throw new InvalidOperationException(
                $"'{binary}' timed out after {timeoutSeconds}s resolving a secret reference.");
        }

        return (stdout, stderr, proc.ExitCode);
    }
}