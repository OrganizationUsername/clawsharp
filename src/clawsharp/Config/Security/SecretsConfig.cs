namespace Clawsharp.Config.Security;

/// <summary>Configuration for at-rest secret encryption and password manager integration.</summary>
public sealed class SecretsConfig
{
    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption for secret fields in config.
    /// When enabled, API keys and tokens are stored as "enc2:&lt;hex&gt;" ciphertext.
    /// Enabled by default.
    /// </summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>
    /// 1Password CLI integration. When any config value begins with "op://",
    /// clawsharp resolves it at startup via the 'op read' command.
    /// Requires OP_SERVICE_ACCOUNT_TOKEN (or a custom env var) to be set.
    /// </summary>
    public OnePasswordConfig? OnePassword { get; set; }

    /// <summary>
    /// Bitwarden Secrets Manager CLI integration. When any config value begins
    /// with "bws:", clawsharp resolves it at startup via 'bws secret get'.
    /// Requires BWS_ACCESS_TOKEN (or a custom env var) to be set.
    /// </summary>
    public BitwardenConfig? Bitwarden { get; set; }
}

/// <summary>Settings for 1Password CLI secret resolution.</summary>
public sealed class OnePasswordConfig
{
    /// <summary>
    /// Name of the environment variable holding the service account token.
    /// Create a token at: https://my.1password.com/developer-tools/infrastructure-secrets/serviceaccount
    /// Default: "OP_SERVICE_ACCOUNT_TOKEN"
    /// </summary>
    public string TokenEnvVar { get; set; } = "OP_SERVICE_ACCOUNT_TOKEN";

    /// <summary>
    /// Path (or name) of the 'op' CLI binary.
    /// Default: "op" — resolved from PATH.
    /// </summary>
    public string CliBinary { get; set; } = "op";

    /// <summary>Maximum seconds to wait for op to respond per secret. Default: 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>Settings for Bitwarden Secrets Manager CLI secret resolution.</summary>
public sealed class BitwardenConfig
{
    /// <summary>
    /// Name of the environment variable holding the machine account access token.
    /// Create a token at: https://vault.bitwarden.com/#/sm (Secrets Manager → Machine Accounts)
    /// Default: "BWS_ACCESS_TOKEN"
    /// </summary>
    public string TokenEnvVar { get; set; } = "BWS_ACCESS_TOKEN";

    /// <summary>
    /// Path (or name) of the 'bws' CLI binary (Bitwarden Secrets Manager, NOT the 'bw' vault CLI).
    /// Default: "bws" — resolved from PATH.
    /// </summary>
    public string CliBinary { get; set; } = "bws";

    /// <summary>Maximum seconds to wait for bws to respond per secret. Default: 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}