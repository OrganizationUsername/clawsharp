using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Slack;

/// <summary>Acknowledgement response sent back to Slack via Socket Mode to confirm receipt.</summary>
internal sealed class SlackAcknowledgeResponse
{
    /// <summary>The envelope ID being acknowledged.</summary>
    [JsonPropertyName("envelope_id")]
    public string EnvelopeId { get; init; } = "";
}