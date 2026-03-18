using Intellenum;

namespace Clawsharp.Channels.Mattermost;

/// <summary>Well-known Mattermost WebSocket event types.</summary>
[Intellenum<string>]
internal partial class MattermostEventType
{
    /// <summary>A new message was posted.</summary>
    public static readonly MattermostEventType Posted = new("posted");
}
