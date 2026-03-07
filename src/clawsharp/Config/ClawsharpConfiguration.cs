using System.Runtime.InteropServices;
using Clawsharp.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Clawsharp.Config.Security;

namespace Clawsharp.Config;

/// <summary>
///     Builds an <see cref="IConfiguration"/> from multiple layered sources.
///     Priority (lowest to highest):
///     <list type="number">
///         <item><c>appsettings.json</c> (optional)</item>
///         <item><c>appsettings.{DOTNET_ENVIRONMENT}.json</c> (optional)</item>
///         <item><c>~/.clawsharp/config.json</c> (legacy home config)</item>
///         <item><c>./config.json</c> (legacy local config)</item>
///         <item><c>CLAWSHARP_CONFIG</c> env var pointing to a JSON file</item>
///         <item><c>.env</c> file in CWD</item>
///         <item>Environment variables prefixed with <c>CLAWSHARP__</c></item>
///     </list>
/// </summary>
public static class ClawsharpConfiguration
{
    /// <summary>Builds the layered <see cref="IConfiguration"/> from all sources.</summary>
    public static IConfiguration Build()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var builder = new ConfigurationBuilder()
                      .SetBasePath(Directory.GetCurrentDirectory())
                      .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                      .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);

        // Legacy config.json paths — home-level first, then local (local wins)
        var homeCfg = ConfigLoader.ExpandHome("~/.clawsharp/config.json");
        if (File.Exists(homeCfg))
        {
            builder.AddJsonFile(homeCfg, optional: true, reloadOnChange: false);
        }

        var localCfg = Path.GetFullPath("config.json");
        if (File.Exists(localCfg))
        {
            builder.AddJsonFile(localCfg, optional: true, reloadOnChange: false);
        }

        // Custom env-var-specified config file
        var customCfgPath = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG");
        if (!string.IsNullOrEmpty(customCfgPath) && File.Exists(customCfgPath))
        {
            builder.AddJsonFile(customCfgPath, optional: true, reloadOnChange: false);
        }

        // .env file
        builder.Add(new DotEnvConfigurationSource(
            Path.Combine(Directory.GetCurrentDirectory(), ".env")));

        // Environment variables — CLAWSHARP__ prefix stripped, double-underscore = hierarchy separator
        builder.AddEnvironmentVariables("CLAWSHARP__");

        return builder.Build();
    }

    /// <summary>
    ///     Convenience method to build config and bind to <see cref="AppConfig"/>.
    ///     Used by CLI commands that run outside the DI host.
    /// </summary>
    public static AppConfig GetAppConfig()
    {
        var configuration = Build();
        var config = configuration.Get<AppConfig>() ?? new AppConfig();
        WarnIfConfigWorldReadable();
        DecryptSecrets(config);
        return config;
    }

    /// <summary>Returns the path to the user-level config file (~/.clawsharp/config.json).</summary>
    public static string GetConfigPath()
        => Path.Combine(ConfigLoader.ExpandHome("~/.clawsharp"), "config.json");

    /// <summary>
    /// Warns on stderr if config.json exists and is world-readable on Unix.
    /// Config files may contain encrypted secrets and should be chmod 600.
    /// </summary>
    internal static void WarnIfConfigWorldReadable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(configPath);
            // Check if other-read (0o004) or other-write (0o002) bits are set
            const UnixFileMode worldReadable = UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
            if ((mode & worldReadable) != 0)
            {
                Console.Error.WriteLine(
                    $"WARNING: {configPath} is world-readable (mode {Convert.ToString((int)mode, 8)}). " +
                    "This file may contain secrets. Run: chmod 600 " + configPath);
            }
        }
        catch (Exception)
        {
            // Best-effort — skip if permission check fails (e.g., on filesystems that don't support Unix modes)
        }
    }

    /// <summary>
    /// Decrypts any "enc2:"-prefixed secret fields in the config object in-place.
    /// Called from <see cref="GetAppConfig"/> (CLI path) and registered as
    /// <c>PostConfigure&lt;AppConfig&gt;</c> in the DI host (gateway path).
    /// </summary>
    /// <remarks>
    /// NOTE: When adding a new channel secret field (e.g., config.Channels.NewChannel.Token),
    /// add a Resolve() call in this method AND add the field key to KnownSecretFields.All
    /// in Cli/KnownSecretFields.cs.
    /// Currently resolved channel fields: Token, BotToken, AppToken, AccessToken, Password,
    /// NickServPassword, Secret, WebhookKey, NostrPrivKey, AppSecret, VerificationToken, PairingToken.
    /// </remarks>
    internal static void DecryptSecrets(AppConfig config)
    {
        var store = new SecretStore(Options.Create(config));
        var pmConfig = config.Secrets ?? new SecretsConfig();

        // Resolve a single field: password manager URIs (op://, bws:) take priority,
        // then enc2: local decryption, then plaintext passthrough.
        string Resolve(string? value)
        {
            if (PasswordManagerResolver.IsReference(value))
            {
                return PasswordManagerResolver.Resolve(value!, pmConfig);
            }

            return store.Decrypt(value);
        }

        foreach (var provider in config.Providers.Values)
        {
            provider.ApiKey = Resolve(provider.ApiKey);
            provider.AwsSecretAccessKey = Resolve(provider.AwsSecretAccessKey);
        }

        if (config.Transcription is { } t)
        {
            t.ApiKey = Resolve(t.ApiKey);
            t.AwsSecretKey = Resolve(t.AwsSecretKey);
        }

        foreach (var channel in config.Channels.Values)
        {
            channel.Token = Resolve(channel.Token);
            channel.BotToken = Resolve(channel.BotToken);
            channel.AppToken = Resolve(channel.AppToken);
            channel.AccessToken = Resolve(channel.AccessToken);
            channel.Password = Resolve(channel.Password);
            channel.NickServPassword = Resolve(channel.NickServPassword);
            channel.Secret = Resolve(channel.Secret);
            channel.WebhookKey = Resolve(channel.WebhookKey);
            channel.NostrPrivKey = Resolve(channel.NostrPrivKey);
            channel.AppSecret = Resolve(channel.AppSecret);
            channel.VerificationToken = Resolve(channel.VerificationToken);
            channel.PairingToken = Resolve(channel.PairingToken);
        }

        // Tool search API keys
        if (config.Tools.Brave is { } brave)
        {
            brave.ApiKey = Resolve(brave.ApiKey);
        }

        if (config.Tools.Exa is { } exa)
        {
            exa.ApiKey = Resolve(exa.ApiKey);
        }

        if (config.Tools.Tavily is { } tavily)
        {
            tavily.ApiKey = Resolve(tavily.ApiKey);
        }

        if (config.Tools.Jina is { } jina)
        {
            jina.ApiKey = Resolve(jina.ApiKey);
        }

        if (config.Tools.Firecrawl is { } firecrawl)
        {
            firecrawl.ApiKey = Resolve(firecrawl.ApiKey);
        }

        if (config.Tools.Perplexity is { } perplexity)
        {
            perplexity.ApiKey = Resolve(perplexity.ApiKey);
        }

        // Embedding API key
        if (config.Memory.Embedding is { } embedding)
        {
            embedding.ApiKey = Resolve(embedding.ApiKey);
        }
    }
}