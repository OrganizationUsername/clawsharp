using Intellenum;

namespace Clawsharp.Analytics;

/// <summary>Discriminator for interaction message rows: sent, received, thinking.</summary>
[Intellenum<string>(conversions: Conversions.SystemTextJson | Conversions.EfCoreValueConverter)]
public partial class MessageType
{
    /// <summary>User prompt sent to the LLM.</summary>
    public static readonly MessageType Sent = new("sent");

    /// <summary>LLM response received.</summary>
    public static readonly MessageType Received = new("received");

    /// <summary>LLM thinking/reasoning content (optional).</summary>
    public static readonly MessageType Thinking = new("thinking");
}
