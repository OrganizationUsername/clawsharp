using Intellenum;

namespace Clawsharp.Channels.Qq;

/// <summary>Well-known OneBot/NapCat post types.</summary>
[Intellenum<string>]
internal partial class QqPostType
{
    /// <summary>A message post.</summary>
    public static readonly QqPostType Message = new("message");
}
