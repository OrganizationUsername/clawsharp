using Intellenum;

namespace Clawsharp.Config.Channels;

/// <summary>Discord guild (server) message policy.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class GroupPolicy
{
    /// <summary>Only respond when @mentioned or !-prefixed (default).</summary>
    public static readonly GroupPolicy Mention = new("mention");

    /// <summary>Respond to all messages in allowed guilds.</summary>
    public static readonly GroupPolicy Open = new("open");
}
