using Intellenum;

namespace Clawsharp.Channels.WeCom;

/// <summary>Well-known WeCom AI Bot message types.</summary>
[Intellenum<string>]
internal partial class WeComMessageType
{
    /// <summary>Plain text message.</summary>
    public static readonly WeComMessageType Text = new("text");

    /// <summary>Voice/audio message.</summary>
    public static readonly WeComMessageType Voice = new("voice");

    /// <summary>Mixed-content message (text + images).</summary>
    public static readonly WeComMessageType Mixed = new("mixed");

    /// <summary>Image attachment.</summary>
    public static readonly WeComMessageType Image = new("image");

    /// <summary>File attachment.</summary>
    public static readonly WeComMessageType File = new("file");
}
