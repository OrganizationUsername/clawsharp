using Intellenum;

namespace Clawsharp.Channels.Line;

/// <summary>Well-known LINE message types.</summary>
[Intellenum<string>]
internal partial class LineMessageType
{
    /// <summary>A text message.</summary>
    public static readonly LineMessageType Text = new("text");
}
