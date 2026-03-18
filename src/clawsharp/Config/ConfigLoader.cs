using System.Text.Json;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Channels;
using Clawsharp.Config.Features;
using Clawsharp.Config.Memory;

namespace Clawsharp.Config;

public static partial class ConfigLoader
{
    public static async Task<AppConfig> LoadAsync(ILogger logger, CancellationToken ct = default)
    {
        var path = ResolvePath();
        if (path is null || !File.Exists(path))
        {
            LogNoConfigFile(logger);
            return DefaultConfig();
        }

        LogLoadingConfig(logger, path);
        var json = await File.ReadAllTextAsync(path, ct);
        json = ConfigMigrator.MigrateLegacyKeys(json, logger);

        try
        {
            var config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig);
            return config ?? DefaultConfig();
        }
        catch (Exception ex)
        {
            // A config file exists but is malformed — fail loudly instead of silently using defaults.
            // (Only fall back to defaults when NO config file is found.)
            throw new InvalidOperationException(
                $"Failed to parse config file at '{path}': {ex.Message}", ex);
        }
    }

    internal static string? ResolvePath()
    {
        var env = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }

        var local = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        if (File.Exists(local))
        {
            return local;
        }

        var home = ExpandHome("~/.clawsharp/config.json");
        return File.Exists(home) ? home : null;
    }

    public static string ExpandHome(string path)
    {
        if (path.StartsWith("~/"))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return path;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "No config file found. Using defaults")]
    private static partial void LogNoConfigFile(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Loading config from {Path}")]
    private static partial void LogLoadingConfig(ILogger logger, string path);

    private static AppConfig DefaultConfig()
    {
        return new()
        {
            Agents = new AgentConfig { Defaults = new AgentDefaults() },
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["ollama"] = new ProviderConfig { Type = "ollama", BaseUrl = ClawsharpConstants.OllamaDefaultBaseUrl }
            },
            Channels = new Dictionary<string, ChannelConfig>
            {
                ["cli"] = new ChannelConfig { Enabled = true }
            },
            Memory = new MemoryConfig(),
            Tools = new ToolsConfig()
        };
    }
}