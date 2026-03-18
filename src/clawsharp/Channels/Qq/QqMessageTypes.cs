using Intellenum;

namespace Clawsharp.Channels.Qq;

/// <summary>Well-known OneBot/NapCat message types.</summary>
[Intellenum<string>]
internal partial class QqMessageType
{
    /// <summary>A group message.</summary>
    public static readonly QqMessageType Group = new("group");

    /// <summary>A text message segment.</summary>
    public static readonly QqMessageType Text = new("text");
}
