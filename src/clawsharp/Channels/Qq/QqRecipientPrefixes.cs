using Intellenum;

namespace Clawsharp.Channels.Qq;

/// <summary>Well-known QQ recipient ID prefixes.</summary>
[Intellenum<string>]
internal partial class QqRecipientPrefix
{
    /// <summary>Prefix for group chat recipient IDs.</summary>
    public static readonly QqRecipientPrefix Group = new("group:");
}
