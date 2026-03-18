using Clawsharp.Core.Utilities;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Memory;
using Clawsharp.Config.Security;
using Clawsharp.Security;

namespace Clawsharp.Config;

/// <summary>
///     Validates an <see cref="AppConfig" /> at startup before the host is built.
///     Returns a list of human-readable error strings (empty = valid).
/// </summary>
public static class ConfigValidator
{
    public static List<string> Validate(AppConfig config)
    {
        var errors = new List<string>();

        // ── Agent defaults ───────────────────────────────────────────────
        var d = config.Agents.Defaults;

        if (!config.Providers.ContainsKey(d.Provider))
        {
            errors.Add($"agents.defaults.provider '{d.Provider}' does not match any key in providers.");
        }

        // ── Provider credentials ───────────────────────────────────────
        foreach (var (provName, provCfg) in config.Providers)
        {
            ValidateProviderCredentials(errors, provName, provCfg);
        }

        if (d.MaxToolIterations <= 0)
        {
            errors.Add($"agents.defaults.maxToolIterations must be > 0 (got {d.MaxToolIterations}).");
        }

        if (d.MaxContextMessages <= 0)
        {
            errors.Add($"agents.defaults.maxContextMessages must be > 0 (got {d.MaxContextMessages}).");
        }

        if (d.ConsolidateEvery <= 0)
        {
            errors.Add($"agents.defaults.consolidateEvery must be > 0 (got {d.ConsolidateEvery}).");
        }

        if (d.Temperature is < 0f or > 2f)
        {
            errors.Add($"agents.defaults.temperature must be in [0.0, 2.0] (got {d.Temperature}).");
        }

        // Validate fallback models reference existing providers
        if (d.FallbackModels is { Count: > 0 })
        {
            foreach (var entry in d.FallbackModels)
            {
                if (string.IsNullOrWhiteSpace(entry.Provider))
                {
                    errors.Add("agents.defaults.fallbackModels: entry has empty provider name.");
                    continue;
                }

                if (!config.Providers.ContainsKey(entry.Provider))
                {
                    errors.Add($"agents.defaults.fallbackModels: '{entry.Provider}' does not match any key in providers.");
                }
            }
        }

        // ── Memory ───────────────────────────────────────────────────────
        var mem = config.Memory;

        if (!MemoryBackend.TryFromValue(mem.Backend, out _))
        {
            errors.Add($"memory.backend must be one of: markdown, sqlite, postgres, mssql (got '{mem.Backend}').");
        }

        if ((mem.Backend == MemoryBackend.Postgres.Value || mem.Backend == MemoryBackend.MsSql.Value)
            && string.IsNullOrWhiteSpace(mem.ConnectionString))
        {
            errors.Add($"memory.connectionString is required for the '{mem.Backend}' backend.");
        }

        // ── Channels ─────────────────────────────────────────────────────
        foreach (var (name, cfg) in config.Channels)
        {
            if (!cfg.Enabled)
            {
                continue;
            }

            var key = name.ToLowerInvariant();
            switch (key)
            {
                case var _ when key == ChannelName.Telegram.Value:
                    if (string.IsNullOrWhiteSpace(cfg.Token))
                    {
                        errors.Add($"channels.{name}: 'token' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Discord.Value:
                    if (string.IsNullOrWhiteSpace(cfg.Token))
                    {
                        errors.Add($"channels.{name}: 'token' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Slack.Value:
                    if (string.IsNullOrWhiteSpace(cfg.BotToken))
                    {
                        errors.Add($"channels.{name}: 'botToken' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.AppToken))
                    {
                        errors.Add($"channels.{name}: 'appToken' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Matrix.Value:
                    if (string.IsNullOrWhiteSpace(cfg.Homeserver))
                    {
                        errors.Add($"channels.{name}: 'homeserver' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.AccessToken))
                    {
                        errors.Add($"channels.{name}: 'accessToken' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Email.Value:
                    if (string.IsNullOrWhiteSpace(cfg.ImapHost))
                    {
                        errors.Add($"channels.{name}: 'imapHost' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.SmtpHost))
                    {
                        errors.Add($"channels.{name}: 'smtpHost' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.Username))
                    {
                        errors.Add($"channels.{name}: 'username' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.Password))
                    {
                        errors.Add($"channels.{name}: 'password' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Irc.Value:
                    if (string.IsNullOrWhiteSpace(cfg.Host))
                    {
                        errors.Add($"channels.{name}: 'host' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Lark.Value:
                    if (string.IsNullOrWhiteSpace(cfg.AppId))
                    {
                        errors.Add($"channels.{name}: 'appId' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.AppSecret))
                    {
                        errors.Add($"channels.{name}: 'appSecret' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Mattermost.Value:
                    if (string.IsNullOrWhiteSpace(cfg.MattermostUrl))
                    {
                        errors.Add($"channels.{name}: 'mattermostUrl' is required.");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.BotToken))
                    {
                        errors.Add($"channels.{name}: 'botToken' is required.");
                    }

                    break;
                case var _ when key == ChannelName.Nostr.Value:
                    if (string.IsNullOrWhiteSpace(cfg.NostrPrivKey))
                    {
                        errors.Add($"channels.{name}: 'nostrPrivKey' is required.");
                    }

                    if (cfg.NostrRelays is null or { Length: 0 })
                    {
                        errors.Add($"channels.{name}: 'nostrRelays' must contain at least one relay URL.");
                    }

                    break;
                case var _ when key == ChannelName.Signal.Value:
                    if (string.IsNullOrWhiteSpace(cfg.BridgeUrl))
                    {
                        errors.Add($"channels.{name}: 'bridgeUrl' is required (signal-cli-rest-api URL).");
                    }

                    break;
                case var _ when key == ChannelName.WhatsApp.Value:
                    if (string.IsNullOrWhiteSpace(cfg.BridgeUrl))
                    {
                        errors.Add($"channels.{name}: 'bridgeUrl' is required (WhatsApp bridge URL).");
                    }

                    break;
                case var _ when key == ChannelName.BlueBubbles.Value:
                    if (string.IsNullOrWhiteSpace(cfg.BridgeUrl))
                    {
                        errors.Add($"channels.{name}: 'bridgeUrl' is required (BlueBubbles server URL).");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.Password))
                    {
                        errors.Add($"channels.{name}: 'password' is required (BlueBubbles server password).");
                    }

                    break;
                case var _ when key == ChannelName.Line.Value:
                    if (string.IsNullOrWhiteSpace(cfg.Secret))
                    {
                        errors.Add($"channels.{name}: 'secret' is required (LINE channel secret).");
                    }

                    if (string.IsNullOrWhiteSpace(cfg.AccessToken))
                    {
                        errors.Add($"channels.{name}: 'accessToken' is required (LINE channel access token).");
                    }

                    break;
                case var _ when key == ChannelName.WeChat.Value:
                    if (string.IsNullOrWhiteSpace(cfg.BridgeUrl))
                    {
                        errors.Add($"channels.{name}: 'bridgeUrl' is required (WeChat bridge URL).");
                    }

                    break;
                // cli, web: no required fields beyond enabled
            }
        }

        // ── Egress policy ────────────────────────────────────────────────────
        if (config.Security?.Egress is { } egress)
        {
            if (egress.Mode == EgressMode.Allowlist && egress.Rules is not { Count: > 0 })
            {
                errors.Add("security.egress: mode is 'allowlist' but no rules are defined — all outbound traffic will be blocked.");
            }

            if (egress.Rules is { Count: > 0 })
            {
                for (var i = 0; i < egress.Rules.Count; i++)
                {
                    var rule = egress.Rules[i];

                    if (string.IsNullOrWhiteSpace(rule.Host))
                    {
                        errors.Add($"security.egress.rules[{i}]: 'host' must not be empty.");
                    }
                    else if (rule.Host != rule.Host.Trim())
                    {
                        errors.Add($"security.egress.rules[{i}]: 'host' has leading or trailing whitespace.");
                    }
                    else if (rule.Host == "*")
                    {
                        errors.Add($"security.egress.rules[{i}]: 'host' value '*' does not match any host. " +
                                   "Use mode 'open' to allow all traffic, or '*.example.com' for wildcard subdomain matching.");
                    }

                    if (rule.Port is < 0 or > 65535)
                    {
                        errors.Add($"security.egress.rules[{i}]: 'port' must be between 1 and 65535 (got {rule.Port}). Omit or set to null for any port.");
                    }
                }
            }
        }

        // ── MCP servers ──────────────────────────────────────────────────────
        if (config.McpServers is { Count: > 0 })
        {
            foreach (var (mcpName, mcpCfg) in config.McpServers)
            {
                var denied = mcpCfg.GetDeniedEnvVarNames();
                foreach (var envVar in denied)
                {
                    errors.Add($"mcpServers.{mcpName}.env: '{envVar}' is a denied environment variable (library injection risk).");
                }
            }
        }

        return errors;
    }

    /// <summary>
    ///     Provider types that require no config credentials (local HTTP servers or OAuth-based).
    /// </summary>
    private static readonly HashSet<string> NoCredentialProviders =
    [
        LlmProviderType.Ollama.Value,
        LlmProviderType.LmStudio.Value,
        LlmProviderType.VLlm.Value,
        LlmProviderType.LlamaCpp.Value,
        LlmProviderType.Copilot.Value
    ];

    /// <summary>
    ///     Validates that the provider has the required credentials for its type.
    ///     Cloud providers need an API key; Bedrock needs AWS credentials; local providers need nothing.
    /// </summary>
    internal static void ValidateProviderCredentials(List<string> errors, string provName, ProviderConfig provCfg)
    {
        var type = provCfg.Type;

        if (NoCredentialProviders.Contains(type))
        {
            return;
        }

        if (type == LlmProviderType.Bedrock.Value)
        {
            if (string.IsNullOrEmpty(provCfg.AwsAccessKeyId))
            {
                errors.Add($"providers.{provName}: 'awsAccessKeyId' is required for type 'bedrock'.");
            }

            if (string.IsNullOrEmpty(provCfg.AwsSecretAccessKey))
            {
                errors.Add($"providers.{provName}: 'awsSecretAccessKey' is required for type 'bedrock'.");
            }

            return;
        }

        // Vertex AI requires a project-specific base URL
        if (type == LlmProviderType.VertexAi.Value && string.IsNullOrWhiteSpace(provCfg.BaseUrl))
        {
            errors.Add(
                $"providers.{provName}: 'baseUrl' is required for type 'vertexai' (e.g. 'https://{{region}}-aiplatform.googleapis.com/v1/projects/{{project}}/locations/{{region}}/endpoints/openapi').");
        }

        // All remaining cloud providers require an API key
        if (string.IsNullOrEmpty(provCfg.ApiKey))
        {
            errors.Add($"providers.{provName}: 'apiKey' is required for type '{type}'.");
        }
    }
}