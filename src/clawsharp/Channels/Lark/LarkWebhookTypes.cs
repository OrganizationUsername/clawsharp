using Intellenum;

namespace Clawsharp.Channels.Lark;

/// <summary>Well-known Lark/Feishu webhook request types.</summary>
[Intellenum<string>]
internal partial class LarkWebhookType
{
    /// <summary>URL verification challenge from Lark platform.</summary>
    public static readonly LarkWebhookType UrlVerification = new("url_verification");
}
