using Intellenum;

namespace Clawsharp.Channels.Line;

/// <summary>Well-known LINE webhook event types.</summary>
[Intellenum<string>]
internal partial class LineEventType
{
    /// <summary>A message event from a user.</summary>
    public static readonly LineEventType Message = new("message");
}
