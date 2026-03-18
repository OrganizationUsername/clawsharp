using Intellenum;

namespace Clawsharp.Channels.Lark;

/// <summary>Well-known Lark/Feishu webhook event types.</summary>
[Intellenum<string>]
internal partial class LarkEventType
{
    /// <summary>An instant message was received (v1 schema).</summary>
    public static readonly LarkEventType ImMessageReceiveV1 = new("im.message.receive_v1");
}
