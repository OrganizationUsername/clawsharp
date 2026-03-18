using Intellenum;

namespace Clawsharp.Security;

/// <summary>Network egress policy mode for outbound HTTP connections.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson)]
public partial class EgressMode
{
    /// <summary>Default — all outbound connections are allowed (current behavior).</summary>
    public static readonly EgressMode Open = new("open");

    /// <summary>Deny-by-default — only hosts matching configured rules are allowed.</summary>
    public static readonly EgressMode Allowlist = new("allowlist");
}
