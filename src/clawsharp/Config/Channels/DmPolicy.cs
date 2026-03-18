using Intellenum;

namespace Clawsharp.Config.Channels;

/// <summary>DM policy for unknown senders on messaging channels.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class DmPolicy
{
    /// <summary>Require a 6-digit pairing code for unknown senders.</summary>
    public static readonly DmPolicy Pairing = new("pairing");

    /// <summary>Only allow senders listed in AllowFrom.</summary>
    public static readonly DmPolicy Allowlist = new("allowlist");

    /// <summary>Allow all DM senders without restriction.</summary>
    public static readonly DmPolicy Open = new("open");
}
