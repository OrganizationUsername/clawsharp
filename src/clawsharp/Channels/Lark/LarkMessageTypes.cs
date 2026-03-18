using Intellenum;

namespace Clawsharp.Channels.Lark;

/// <summary>Well-known Lark/Feishu message types.</summary>
[Intellenum<string>]
internal partial class LarkMessageType
{
    /// <summary>A plain text message.</summary>
    public static readonly LarkMessageType Text = new("text");
}
