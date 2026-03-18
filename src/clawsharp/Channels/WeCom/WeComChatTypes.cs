using Intellenum;

namespace Clawsharp.Channels.WeCom;

/// <summary>Well-known WeCom chat types.</summary>
[Intellenum<string>]
internal partial class WeComChatType
{
    /// <summary>A group chat context.</summary>
    public static readonly WeComChatType Group = new("group");
}
