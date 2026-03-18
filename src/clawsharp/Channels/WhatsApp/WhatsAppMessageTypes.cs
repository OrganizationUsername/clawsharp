using Intellenum;

namespace Clawsharp.Channels.WhatsApp;

/// <summary>Well-known WhatsApp message types.</summary>
[Intellenum<string>]
internal partial class WhatsAppMessageType
{
    /// <summary>Push-to-talk voice message.</summary>
    public static readonly WhatsAppMessageType PushToTalk = new("ptt");

    /// <summary>Audio message.</summary>
    public static readonly WhatsAppMessageType Audio = new("audio");
}
