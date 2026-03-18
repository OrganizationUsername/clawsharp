using Intellenum;

namespace Clawsharp.Channels.Signal;

/// <summary>Well-known Signal CLI SSE event types.</summary>
[Intellenum<string>]
internal partial class SignalSseEventType
{
    /// <summary>An incoming message was received.</summary>
    public static readonly SignalSseEventType Receive = new("receive");
}
