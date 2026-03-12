using Clawsharp.Config;
using Clawsharp.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Security;

/// <summary>
///     Injectable singleton that owns the <see cref="WebPairingGuard" /> instance for the Web channel.
///     Provides a shared access point for both <c>WebChannel</c> and <c>GatewayIpcService</c>.
///     Created when the Web channel is enabled and no static pairing token is configured.
/// </summary>
public sealed class WebPairingService
{
    private readonly WebPairingGuard? _guard;

    public WebPairingService(IOptions<AppConfig> options, ILogger<WebPairingService> logger)
    {
        var cfg = options.Value.Channels.GetValueOrDefault(ChannelName.Web.Value);
        if (cfg is { Enabled: true } && cfg.PairingToken is not { Length: > 0 })
        {
            var persistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".clawsharp", "web-paired-tokens.json");
            _guard = new WebPairingGuard(persistPath, logger);
        }
    }

    /// <summary>True when dynamic pairing is active (Web channel enabled, no static token).</summary>
    public bool IsEnabled => _guard is not null;

    /// <summary>Current one-time pairing code. Null when already paired or pairing is disabled.</summary>
    public string? PairingCode => _guard?.PairingCode;

    /// <summary>True once at least one client has successfully paired.</summary>
    public bool HasPairedClients => _guard?.HasPairedClients ?? false;

    /// <summary>Generates a fresh pairing code, replacing any existing one. Returns the new code.</summary>
    public string? Regenerate() => _guard?.RegenerateCode();

    /// <inheritdoc cref="WebPairingGuard.IsAuthenticated"/>
    public bool IsAuthenticated(string token) => _guard?.IsAuthenticated(token) ?? false;

    /// <inheritdoc cref="WebPairingGuard.TryPair"/>
    public string? TryPair(string clientIp, string code) => _guard?.TryPair(clientIp, code);
}