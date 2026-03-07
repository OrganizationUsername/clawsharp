namespace Clawsharp.Cli.Config;

/// <summary>
/// Canonical set of JSON property names that hold secrets in config.json.
/// Used by <see cref="ConfigSetCommand"/> (auto-encrypt on set),
/// <see cref="EncryptSecretsCommand"/> (bulk encrypt), and
/// <see cref="OnboardCommand"/> (encrypt during onboarding).
/// </summary>
public static class KnownSecretFields
{
    /// <summary>All config keys whose values must be encrypted at rest.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey", // provider, tool, embedding, and transcription API keys
        "awsSecretAccessKey", // AWS IAM secret access key
        "awsSecretKey", // transcription.awsSecretKey
        "token", // generic token fields
        "botToken", // Telegram, Discord bot tokens
        "appToken", // Slack app-level token
        "accessToken", // Matrix, OAuth access tokens
        "pairingToken", // web channel static pairing token
        "password", // email (IMAP/SMTP) password
        "nickServPassword", // IRC NickServ password
        "transcriptionApiKey", // legacy/explicit transcription key
        "secret", // generic secret fields
        "webhookKey", // webhook signing/verification keys
        "nostrPrivKey", // Nostr private key (nsec1 or hex)
        "appSecret", // Lark/Feishu app secret
        "verificationToken", // Lark/Feishu webhook verification token
    };
}