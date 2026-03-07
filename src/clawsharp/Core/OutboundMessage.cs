using Clawsharp.Core.Utilities;

namespace Clawsharp.Core;

/// <summary>A message to be sent to a channel (assistant output).</summary>
public sealed record OutboundMessage(
    /// <summary>The target channel name.</summary>
    ChannelName Channel,
    /// <summary>Identifier of the recipient (chat or user ID).</summary>
    string RecipientId,
    /// <summary>The text content of the message.</summary>
    string Text,
    /// <summary>Optional thread identifier for threaded replies.</summary>
    string? ThreadId = null
);