using Microsoft.Extensions.Configuration;

namespace Clawsharp.Config;

/// <summary>
///     Custom <see cref="IConfigurationSource"/> that reads a <c>.env</c> file
///     and exposes each <c>KEY=value</c> pair as a flat configuration key.
/// </summary>
internal sealed class DotEnvConfigurationSource(string path) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new DotEnvConfigurationProvider(path);
}

internal sealed class DotEnvConfigurationProvider(string path) : ConfigurationProvider
{
    public override void Load()
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();

            // Strip optional surrounding quotes (double or single)
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1];
            }

            Data[key] = value;
        }
    }
}